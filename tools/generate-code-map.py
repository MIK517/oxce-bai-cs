#!/usr/bin/env python3
"""Generates docs/code-map.md: a navigational index of the repository.

The index lists every project with its namespaces and public types (one line each, with
the file path), the shared and per-project test helpers, and which fixture manifest and
expected-output files each test class consumes.

The C# scan is a lightweight line-oriented parser (no Roslyn), sufficient for the
repository's conventions: file-scoped namespaces and four-space indentation.

Usage (from any directory):
    python3 tools/generate-code-map.py            # writes docs/code-map.md
    python3 tools/generate-code-map.py --check    # exits 1 when the file is stale
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "docs" / "code-map.md"
PROJECT_GROUPS = ("src", "tools", "tests", "benchmarks")

TYPE_RE = re.compile(
    r"^(?P<indent>[ \t]*)(?P<mods>(?:(?:public|internal|protected|private|file|static|sealed|abstract|"
    r"readonly|partial|ref|unsafe|new)\s+)*)"
    r"(?P<kind>record\s+struct|record\s+class|class|struct|interface|enum|record|delegate)\s+"
    r"(?P<rest>.*)$"
)
NAME_RE = re.compile(r"(?P<name>@?[A-Za-z_][A-Za-z0-9_]*)(?P<generic><[^>{(]*>)?")
DELEGATE_RE = re.compile(r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)(?P<generic><[^>(]*>)?\s*\(")
NAMESPACE_RE = re.compile(r"^\s*namespace\s+([A-Za-z0-9_.]+)\s*[;{]?")
SUMMARY_RE = re.compile(r"^\s*///\s?(.*)$")
TEST_ATTR_RE = re.compile(r"\[(Fact|Theory)\b")
STRING_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')


@dataclass
class TypeInfo:
    name: str
    kind: str
    visibility: str
    path: str
    line: int
    summary: str


@dataclass
class ProjectInfo:
    name: str
    path: str
    references: list[str] = field(default_factory=list)
    namespaces: dict[str, list[TypeInfo]] = field(default_factory=lambda: defaultdict(list))
    file_count: int = 0
    line_count: int = 0


def tracked_files() -> list[str]:
    """Uses git so ignored build output, private fixtures and scratch files never leak in."""
    try:
        result = subprocess.run(
            ["git", "-c", "core.quotepath=false", "ls-files", "--cached", "--others", "--exclude-standard"],
            cwd=ROOT, capture_output=True, text=True, check=True, encoding="utf-8",
        )
        files = [line for line in result.stdout.splitlines() if line]
    except (OSError, subprocess.CalledProcessError):
        files = [
            p.relative_to(ROOT).as_posix()
            for p in ROOT.rglob("*")
            if p.is_file() and not ({"bin", "obj", ".git", "artifacts"} & set(p.parts))
        ]
    return sorted({f for f in files if (ROOT / f).is_file()}, key=str)


def summary_text(lines: list[str]) -> str:
    text = " ".join(line.strip() for line in lines)
    text = re.sub(r"</?summary>", "", text)
    text = re.sub(r'<(?:see|seealso|paramref|typeparamref)\s+\w+="(?:[A-Z]:)?([^"]+)"\s*/>', r"`\1`", text)
    text = re.sub(r"<c>(.*?)</c>", r"`\1`", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"\s+", " ", text).strip()
    match = re.match(r"(.+?\.)(\s|$)", text)
    if match:
        text = match.group(1)
    if len(text) > 140:
        text = text[:137].rstrip() + "..."
    return text.replace("|", "\\|")


def scan_types(rel_path: str) -> tuple[str | None, list[TypeInfo], list[str]]:
    """Returns (namespace, types, lines). Nested types are reported as Outer.Inner."""
    text = (ROOT / rel_path).read_text(encoding="utf-8-sig", errors="replace")
    lines = text.splitlines()
    namespace = None
    types: list[TypeInfo] = []
    stack: list[tuple[int, str, str]] = []  # (indent, name, visibility)
    doc: list[str] = []
    in_block_comment = False
    base_indent = 0
    for index, raw in enumerate(lines, start=1):
        stripped = raw.strip()
        if in_block_comment:
            if "*/" in stripped:
                in_block_comment = False
            continue
        if stripped.startswith("/*") and "*/" not in stripped:
            in_block_comment = True
            continue
        doc_match = SUMMARY_RE.match(raw)
        if doc_match:
            doc.append(doc_match.group(1))
            continue
        if not stripped or stripped.startswith("[") or stripped.startswith("//") or stripped.startswith("#"):
            continue
        ns_match = NAMESPACE_RE.match(raw)
        if ns_match and namespace is None:
            namespace = ns_match.group(1)
            if not stripped.endswith(";"):
                base_indent = 4
            doc = []
            continue
        match = TYPE_RE.match(raw)
        if match and not re.search(r"\bnew\s*\(", stripped):
            mods = match.group("mods").split()
            kind = re.sub(r"\s+", " ", match.group("kind"))
            rest = match.group("rest")
            name_match = (DELEGATE_RE.search(rest) if kind == "delegate" else NAME_RE.match(rest))
            is_decl = name_match is not None and (
                kind == "delegate" or not re.match(r"^\s*=", rest[name_match.end():])
            )
            if is_decl and (mods or index == 1 or len(raw) - len(raw.lstrip()) <= base_indent + 12):
                indent = len(raw.expandtabs(4)) - len(raw.expandtabs(4).lstrip())
                while stack and stack[-1][0] >= indent:
                    stack.pop()
                if "public" in mods and "protected" not in mods:
                    visibility = "public"
                elif "private" in mods or "protected" in mods:
                    visibility = "private"
                elif "file" in mods:
                    visibility = "file"
                else:
                    visibility = "internal"
                name = name_match.group("name") + (name_match.group("generic") or "")
                name = re.sub(r"\s+", " ", name)
                qualified = ".".join([s[1] for s in stack] + [name])
                # A nested type is only as visible as its least visible container.
                effective = visibility
                for _, _, outer in stack:
                    if outer != "public":
                        effective = outer
                types.append(TypeInfo(qualified, "record struct" if kind == "record struct" else kind.split()[0],
                                      effective, rel_path, index, summary_text(doc)))
                if kind != "delegate" and not stripped.endswith(";"):
                    stack.append((indent, name.split("<")[0], effective))
        doc = []
    return namespace, types, lines


def project_references(csproj: Path) -> list[str]:
    text = csproj.read_text(encoding="utf-8-sig")
    refs = re.findall(r'<ProjectReference\s+Include="([^"]+)"', text)
    return sorted({Path(r.replace("\\", "/")).stem for r in refs}, key=str)


def collect_projects(files: list[str]) -> list[ProjectInfo]:
    projects = []
    for csproj in sorted((f for f in files if f.endswith(".csproj")), key=str):
        top = csproj.split("/")[0]
        if top not in PROJECT_GROUPS:
            continue
        directory = str(Path(csproj).parent.as_posix())
        project = ProjectInfo(Path(csproj).stem, directory, project_references(ROOT / csproj))
        projects.append(project)
    by_dir = sorted(projects, key=lambda p: -len(p.path))
    for rel in files:
        if not rel.endswith(".cs"):
            continue
        owner = next((p for p in by_dir if rel.startswith(p.path + "/")), None)
        if owner is None:
            continue
        namespace, types, lines = scan_types(rel)
        owner.file_count += 1
        owner.line_count += len(lines)
        for info in types:
            owner.namespaces[namespace or "(global)"].append(info)
    return projects


def linked_sources(files: list[str]) -> dict[str, list[str]]:
    """Maps shared source files (<Compile Include=...> links) to the projects compiling them."""
    result: dict[str, list[str]] = defaultdict(list)
    for csproj in (f for f in files if f.endswith(".csproj")):
        text = (ROOT / csproj).read_text(encoding="utf-8-sig")
        for include in re.findall(r'<Compile\s+Include="([^"]+)"', text):
            pattern = (Path(csproj).parent / include.replace("\\", "/")).as_posix()
            base = ROOT / Path(csproj).parent
            targets = sorted(base.glob(include.replace("\\", "/"))) if "*" in pattern else [ROOT / pattern]
            for target in targets:
                try:
                    rel = target.resolve().relative_to(ROOT).as_posix()
                except ValueError:
                    continue
                if target.is_file():
                    result[rel].append(Path(csproj).stem)
    return result


def test_classes(files: list[str]) -> dict[str, str]:
    """Maps test file -> first public class name for files that contain tests."""
    result = {}
    for rel in files:
        if not (rel.startswith("tests/") and rel.endswith(".cs")):
            continue
        text = (ROOT / rel).read_text(encoding="utf-8-sig", errors="replace")
        if TEST_ATTR_RE.search(text):
            match = re.search(r"\bclass\s+([A-Za-z0-9_]+)", text)
            result[rel] = match.group(1) if match else Path(rel).stem
    return result


def fixture_usage(files: list[str], tests: dict[str, str], helpers: list[str]):
    manifests = []
    for rel in files:
        if rel.startswith("fixtures/manifests/") and rel.endswith(".json"):
            data = json.loads((ROOT / rel).read_text(encoding="utf-8-sig"))
            manifests.append((rel, data))
    test_text = {rel: (ROOT / rel).read_text(encoding="utf-8-sig", errors="replace")
                 for rel in sorted(set(tests) | set(helpers), key=str)}
    literals = {rel: set(STRING_RE.findall(text)) for rel, text in test_text.items()}

    def users_of(tokens: set[str], allow_substring: set[str]) -> list[str]:
        hits = []
        for rel, strings in literals.items():
            if strings & tokens or any(tok in test_text[rel] for tok in allow_substring):
                hits.append(rel)
        return sorted(hits, key=str)

    manifest_rows = []
    covered_expected = set()
    for rel, data in manifests:
        manifest_id = data.get("id", Path(rel).stem)
        expected = data.get("expected", "")
        covered_expected.add(expected)
        expected_name = Path(expected).name
        users = users_of({manifest_id, Path(rel).stem}, {expected_name} if expected_name else set())
        manifest_rows.append((rel, manifest_id, expected, data.get("reference", {}).get("kind", ""), users))

    orphan_rows = []
    for rel in files:
        if rel.startswith("fixtures/expected/") and rel not in covered_expected:
            users = users_of(set(), {Path(rel).name})
            orphan_rows.append((rel, users))

    mod_rows = []
    mods_root = "fixtures/public/mods/"
    fixtures = sorted({f[len(mods_root):].split("/")[0] for f in files if f.startswith(mods_root)}, key=str)
    for name in fixtures:
        users = users_of({name}, {f'mods/{name}"', f'mods/{name}/'})
        mod_rows.append((mods_root + name, users))
    return manifest_rows, orphan_rows, mod_rows


def test_link(rel: str, tests: dict[str, str]) -> str:
    kind = "benchmark" if rel.startswith("benchmarks/") else "helper"
    label = tests.get(rel) or f"{Path(rel).stem} ({kind})"
    return f"`{label}` ([{Path(rel).name}](../{rel}))"


def render(files: list[str]) -> str:
    projects = collect_projects(files)
    tests = test_classes(files)
    links = linked_sources(files)
    out: list[str] = []
    w = out.append
    w("# Code map")
    w("")
    w("<!-- Generated by tools/generate-code-map.py. Do not edit by hand; rerun the script. -->")
    w("")
    w("Navigational index of projects, namespaces, public types, test helpers and fixture ownership.")
    w("Regenerate with `python3 tools/generate-code-map.py` after adding types, helpers or fixtures;")
    w("`--check` reports a stale map. Types are listed with their first XML-doc sentence when present.")
    w("")
    w("## Projects")
    w("")
    w("| Project | Files | Lines | References |")
    w("| --- | ---: | ---: | --- |")
    for p in projects:
        refs = ", ".join(r.removeprefix("Oxce.") for r in p.references) or "-"
        w(f"| [{p.name}](#{p.name.lower().replace('.', '')}) | {p.file_count} | {p.line_count} | {refs} |")
    w("")

    for p in projects:
        is_test = p.path.startswith("tests/") or p.path.startswith("benchmarks/")
        has_public = any(t.visibility == "public" for ts in p.namespaces.values() for t in ts)
        if not is_test and not has_public:
            w(f"## {p.name}")
            w("")
            w(f"`{p.path}` - {p.file_count} files, {p.line_count} lines. No public API; top-level types:")
            w("")
            for namespace in sorted(p.namespaces, key=str):
                for t in sorted(p.namespaces[namespace], key=lambda t: (t.path, t.line)):
                    if "." not in t.name:
                        desc = f" - {t.summary}" if t.summary else ""
                        qualified = t.name if namespace == "(global)" else f"{namespace}.{t.name}"
                        w(f"- `{qualified}` ({t.visibility} {t.kind}) "
                          f"[{t.path.split('/')[-1]}](../{t.path}#L{t.line}){desc}")
            w("")
            continue
        w(f"## {p.name}")
        w("")
        w(f"`{p.path}` - {p.file_count} files, {p.line_count} lines.")
        w("")
        for namespace in sorted(p.namespaces, key=str):
            types = p.namespaces[namespace]
            if is_test:
                shown = [t for t in types if "." not in t.name and t.path not in tests]
            else:
                shown = [t for t in types if t.visibility == "public"]
            if not shown:
                continue
            w(f"### `{namespace}`")
            w("")
            for t in sorted(shown, key=lambda t: (t.path, t.line)):
                vis = "" if t.visibility == "public" else f"{t.visibility} "
                desc = f" - {t.summary}" if t.summary else ""
                w(f"- `{t.name}` ({vis}{t.kind}) [{t.path.split('/')[-1]}](../{t.path}#L{t.line}){desc}")
            w("")
        if is_test and p.path.startswith("tests/"):
            test_files = sorted((f for f in tests if f.startswith(p.path + "/")), key=str)
            if test_files:
                w(f"<details><summary>{len(test_files)} test classes</summary>")
                w("")
                for rel in test_files:
                    w(f"- {test_link(rel, tests)}")
                w("")
                w("</details>")
                w("")

    w("## Test helpers")
    w("")
    w("Shared sources compiled into several projects, and non-test types that tests reuse.")
    w("Prefer these over local copies (see AGENTS.md, *Test conventions*).")
    w("")
    helper_files = sorted(
        {f for f in files if f.startswith("tests/") and f.endswith(".cs") and "/obj/" not in f}
        - set(tests.keys()) | {f for f in links if f.startswith("tests/")},
        key=str,
    )
    for rel in helper_files:
        users = links.get(rel)
        linked = f" - linked into {', '.join(sorted(users))}" if users else ""
        w(f"### [{rel}](../{rel}){linked}")
        w("")
        _, types, lines = scan_types(rel)
        members = [
            (i + 1, m.group(1))
            for i, line in enumerate(lines)
            if (m := re.match(r"^\s{4,8}(?:internal|public)\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\](),.? ]+?\s+(\w+)\s*(?:[({=]|=>)", line))
            and not re.search(r"\b(class|struct|record|enum|interface)\b", line)
        ]
        for t in types:
            w(f"- `{t.name}` ({t.kind}, line {t.line}){' - ' + t.summary if t.summary else ''}")
        if members:
            names = ", ".join(f"`{name}`" for _, name in dict.fromkeys(members))
            w(f"- members: {names}")
        w("")

    # Test files that also declare reusable nested helpers (e.g. temporary directories).
    nested = []
    for rel in sorted(tests, key=str):
        _, types, _ = scan_types(rel)
        for t in types:
            if t.kind in ("class", "struct", "record") and t.name.count(".") >= 1 and \
                    re.search(r"(Fixture|Directory|Helper|Builder|Stub|Fake|Recorder|Scope|Content)", t.name):
                nested.append((rel, t))
    if nested:
        w("### Helpers nested in test classes")
        w("")
        for rel, t in nested:
            w(f"- `{t.name}` ({t.visibility} {t.kind}) [{Path(rel).name}](../{rel}#L{t.line})")
        w("")

    benchmark_files = [f for f in files if f.startswith("benchmarks/") and f.endswith(".cs")]
    manifest_rows, orphan_rows, mod_rows = fixture_usage(files, tests, helper_files + benchmark_files)
    w("## Fixture manifests")
    w("")
    w("Each manifest pins inputs and a reference oracle (`fixtures/manifests/*.json`). Every manifest is")
    w("also hash-verified by `FixturePipelineTests.EveryManifestIsValidAndReferencesPinnedFiles`.")
    w("A test is listed when it names the manifest id or its expected file.")
    w("")
    w("| Manifest | Reference | Expected | Tests |")
    w("| --- | --- | --- | --- |")
    for rel, manifest_id, expected, kind, users in manifest_rows:
        user_text = "<br>".join(test_link(u, tests) for u in users) or "**none**"
        w(f"| [{manifest_id}](../{rel}) | {kind} | `{expected.removeprefix('fixtures/expected/')}` | {user_text} |")
    w("")
    if orphan_rows:
        w("### Expected outputs without a manifest")
        w("")
        w("| Expected | Tests |")
        w("| --- | --- |")
        for rel, users in orphan_rows:
            user_text = "<br>".join(test_link(u, tests) for u in users) or "**none**"
            w(f"| `{rel.removeprefix('fixtures/expected/')}` | {user_text} |")
        w("")
    w("### Public mod fixtures")
    w("")
    w("| Fixture | Consumers |")
    w("| --- | --- |")
    for rel, users in mod_rows:
        user_text = "<br>".join(test_link(u, tests) for u in users) or "**none**"
        w(f"| `{rel.removeprefix('fixtures/public/')}` | {user_text} |")
    w("")
    return "\n".join(out)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="fail when docs/code-map.md is out of date")
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()
    content = render(tracked_files())
    if args.check:
        current = args.output.read_text(encoding="utf-8") if args.output.exists() else ""
        if current.replace("\r\n", "\n") != content:
            print(f"{args.output} is stale; run tools/generate-code-map.py", file=sys.stderr)
            return 1
        return 0
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(content, encoding="utf-8", newline="\n")
    print(f"wrote {args.output.relative_to(ROOT) if args.output.is_relative_to(ROOT) else args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

# ADR 0016: Packed script programs and reusable execution frames

## Status

Accepted for the post-Phase-4 runtime foundation.

## Context

The Phase 4 semantic IR stored every instruction as an object, every operand list in a
separate array and read-only wrapper, and the source span on the hot instruction. Each
execution then allocated register arrays, output metadata, a binding dictionary,
diagnostic and trace lists, and result dictionaries. This was a useful compatibility
bootstrap, but it made script-heavy tactical and event execution unnecessarily
allocation-sensitive.

The reference engine reuses a bounded `ScriptWorkerBase` register block, stores compact
procedure data, performs nested calls through the worker, and copies writable outputs
back at the execution boundary. Its native byte stream contains function pointers and
C++ layout details that are not a portable managed ABI.

## Decision

`ScriptProgram` owns three immutable runtime tables: packed instruction headers, one
flat operand array, and a source-span side table. Host-call operands contain dense
per-program binding slots resolved during construction. Separator parameters remain a
compile-time matching concern and consume no runtime operand storage. The public
instruction list is a tooling adapter that materializes semantic instruction objects
on demand; the VM never uses it.

The hot API executes positional `ScriptRuntimeValue` or scalar spans through a caller-
owned `ScriptExecutionFrame`. A prepared frame lazily owns bounded register and scratch
arrays for scalar, text, reference, nested-call, and event execution. Frames are
reusable and deliberately not thread-safe. A caller uses one frame per concurrent
execution chain.

Successful calls commit output spans. Operation, provider, recursion, trace, or event
failure leaves caller-owned output spans unchanged. Writable host arguments are copied
back only after a provider succeeds. Context-aware providers may execute nested
programs through the same frame; call depth is bounded and nested failure status and
diagnostic identity propagate through providers. Scratch and register spans are
cleared at their lifetime boundary so a reusable frame does not retain host references.

The existing dictionary/result API remains an allocating adapter over the same packed
VM. Optional traces are delivered through an explicit sink and retain stable semantic
instruction index, operation, source span, result, and success fields.

## Consequences

- Prepared, non-traced scalar and scalar-host execution can allocate zero managed
  bytes per call.
- Gameplay providers can pass text and object references without encoding them as
  integers or exposing gameplay types to the compiler.
- Event chains can reuse one frame and commit their final outputs transactionally.
- Tooling that enumerates `ScriptProgram.Instructions` allocates semantic views; it is
  not a runtime API.
- The ABI remains platform-independent and does not reproduce native function-pointer
  bytecode.

## Reference mapping

- `src/Engine/Script.h`: `ScriptWorkerBase`, `ScriptWorker::execute`, register set/get/
  reset, `ScriptText`, and `ScriptValueData`.
- `src/Engine/Script.cpp`: `scriptExe`, `call_func_h`, `executeBase`, text operations,
  and operation failure handling.
- `src/Engine/ScriptBind.h`: dynamic/direct argument extraction and writable argument
  binding.

# 0031: Persistent alien-base birth identity

Status: accepted for Phase 6 branch 4c.

## Problem

Alien-base marker IDs are gameplay counters and can be reused after deletion.
Matching unknown save sidecars by deployment and marker ID makes a newly created
base inherit the deleted base's fields if creation precedes the next rewrite.
The operation-base batch audit reproduced this with both fresh and cached rules.

## Decision

World gameplay owns an unsigned 64-bit alien-base birth sequence, separate from
gameplay counters. Successful operation-base creation increments it and uses
alien-base:generated:<sequence> as the existing persistent sidecar key.
Failed searches do not increment it. Surviving keys remain stable across upgrades.

WorldSnapshot carries the sequence. The save adapter owns the optional body key
oxcePortAlienBaseBirthSequence and retains it even after all bases are deleted.
Legacy saves start at zero and keep their imported identity keys. The reference
engine ignores the port extension; its established alien-base fields are unchanged.

Preflight reserves sequence capacity for the whole possible creation batch before
RNG or mutation. Restore rejects generated identities beyond the saved sequence.
Gameplay counters cannot modify this sequence. Repeated rewrites and deletion,
marker reuse, reload, exhaustion and malformed sequence tests cover the contract.

This is the base counterpart of the occurrence identity decision in ADR 0028.
Future alien-base creation callers must use the same world-owned birth operation.

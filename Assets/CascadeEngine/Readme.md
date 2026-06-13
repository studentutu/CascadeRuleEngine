# Cascade Rule Engine

## Purpose

Cascade is a small fact-reduction pipeline that replaces ECS-style hidden
execution order and ecs specific issues (inability to resolute request in a single execution, constant maintance of all systems and beware af all systems query in order no to be in a performance trap) with one explicit, inspectable data flow:

```text
input/events
  -> facts (typed payloads, queued)
  -> reducers (read committed, write other facts and reduce till commit change)
  -> commit (staged -> committed, per declared property/state)
  -> output state mutations (typed published output: entity, previous, next)
  -> target consumers (UI, audio, ECS bridge, network can poll on the entity and it's state, this is not part of the package)
```

The package boundary is `Assets/CascadeEngine`. It lifts into any Unity
project as-is. It does not own UI, audio, replication, or after-pass dispatch;
hosts consume the mutation output and route it themselves.

The mental model is React/Virtual-DOM: facts are events, reducers are pure
state transitions, commit is reconciliation with one default diff rule plus
declared policies, and mutations are the typed diff that consumers render
from.

## Quick Start (from the Hestia sample)

### 1. Declare a schema

One schema object per engine. It owns every key: indices are auto-assigned,
duplicate names fail fast at construction, and each fact kind is permanently
bound to exactly one reducer.

```csharp
public sealed class HestiaGameCascadeSchema
{
    public HestiaGameCascadeSchema(int entityCapacity)
    {
        Schema = new CascadeSchema(entityCapacity);

        AcceptsAmmoInput = Schema.AddFlag("AcceptsAmmoInput");

        AmmoCurrent = Schema.AddProperty<int>("AmmoCurrent");                          // default: commit if changed
        Position = Schema.AddProperty<float>("Position", AreClosePositions);           // custom equality (epsilon)
        PublishedAudioCues = Schema.AddMarkerProperty<CascadeSignal>("PublishedAudioCues"); // publish even if unchanged

        AmmoSpendRequested = Schema.AddFact<int>("AmmoSpendRequested", ReduceAmmoSpendRequested);
        DryFireCue = Schema.AddFact<CascadeSignal>("DryFireCue", ReduceAudioCue);
    }

    public CascadeSchema Schema { get; }
    public CascadeEntityFlagKey AcceptsAmmoInput { get; }
    public CascadeProperty<int> AmmoCurrent { get; }
    public CascadeProperty<float> Position { get; }
    public CascadeProperty<CascadeSignal> PublishedAudioCues { get; }
    public CascadeFact<int> AmmoSpendRequested { get; }
    public CascadeFact<CascadeSignal> DryFireCue { get; }
    // reducers below ...
}
```

`CascadeProperty<T>` ties the key to its value type. `Stage(AmmoCurrent, 5)`
compiles only for `int`; there is no runtime unwrap and no boxing.

### 2. Write reducers

Reducers receive the typed payload, read committed (or staged-this-tick)
state, stage candidate values, and may produce follow-up facts. They never
touch committed state or host systems.

```csharp
private void ReduceAmmoSpendRequested(CascadeReducerContext context, int amount)
{
    if (!context.HasFlag(AcceptsAmmoInput))
    {
        return;
    }

    var nextAmmo = Math.Max(0, context.Read(AmmoCurrent) - amount);   // typed read, no generic argument
    context.Stage(AmmoCurrent, nextAmmo);                             // typed write, no allocation
    context.Stage(AmmoEmpty, nextAmmo <= 0);

    if (nextAmmo <= 0)
    {
        context.Produce(DryFireCue);                                  // signal fact, no payload noise
    }
}
```

Same-property conflicts resolve at staging time via priority (higher wins,
equal overwrites). Priority lives in the payload of facts that need it:

```csharp
private void ReduceDesiredPosition(CascadeReducerContext context, HestiaMoveRequest request)
{
    context.StageIfPriorityAtLeast(Position, request.Position, request.Priority);
}
```

### 3. Build the engine

The engine is sealed and constructed from the schema (composition, not
inheritance). Constructing the engine seals the schema; late declarations
throw.

```csharp
var schema = new HestiaGameCascadeSchema(entityCapacity: 1000);
var engine = new CascadeEngine(schema.Schema, maxReducerRunsPerTick: 64);
```

A thin facade per project keeps call sites domain-shaped (see
`Assets/HestiaGame/HestiaGameCascade.cs`), but every engine capability is
public - no hand-written getter per property is required.

### 4. Drive it and consume mutations

```csharp
engine.SetFlag(entityId, schema.AcceptsAmmoInput);
engine.SetCommitted(entityId, schema.AmmoCurrent, 30);     // initialization, publishes nothing

engine.EnqueueFact(entityId, schema.AmmoSpendRequested, 1); // host input -> typed fact
engine.RunTick();

// Typed routing: previous and next values included, no re-read needed.
engine.ForEachMutation(schema.AmmoCurrent, (entity, previous, next) => hud.SetAmmo(entity, next));

// Aggregate / exact checks and the untyped drain are also available:
if (engine.WasPropertyMutated(schema.AmmoEmpty)) { /* ... */ }
for (var i = 0; i < engine.MutationCount; i++) { var m = engine.GetMutation(i); }

// Committed truth is always readable, typed:
var ammo = engine.ReadCommitted(entityId, schema.AmmoCurrent);
```

Mutation output stays readable until the next `RunTick` (or explicit
`ClearMutations`). Cache the `ForEachMutation` handler delegate in a field to
keep consumption allocation-free.

## Pipeline Rules

```text
Reducers never mutate committed state.
Reducers read committed (or staged-this-tick) state and the typed payload.
Reducers write staged values and/or produce more facts.
Reducer order must not matter; only commit changes committed state.
Commit runs once per property column, applying that property's change policy.
Commit publishes typed mutations (entity, previous, next) as the only core output.
```

Commit policy is schema data, declared once per property:

```text
AddProperty<T>(name)            commit if changed (EqualityComparer<T>.Default)
AddProperty<T>(name, areEqual)  commit if changed by custom equality (e.g. epsilon)
AddMarkerProperty<T>(name)      commit and publish on every staged write (cues/markers)
```

Guards and failure policy:

```text
schema construction        duplicate names, null reducers, bad capacities throw at startup
engine construction        seals the schema; one schema serves exactly one engine
key ownership              property/fact keys from another schema throw on use
maxReducerRunsPerTick      throws when a fact loop cascades past the budget
fact capacity              per-kind queue capacity declared at AddFact; overflow throws
on any tick exception      staged work aborted, mutations cleared, error surfaced
```

Destroyed entities are skipped in the fact loop, their staged work is dropped
at commit, and `DestroyEntity` is allowed from reducers and from the host.
Destruction is permanent: ids are dense host-managed indices, not recycled
handles.

Relevance is a host decision made before producing a fact: if the work does
not change authoritative truth and the entity is not relevant to the local
projection, call `SkipNonRelevant()` instead of enqueueing. Core only counts
the skips.

## Primitives

| Type | Responsibility |
| --- | --- |
| `CascadeEntityId` | dense entity index (struct) |
| `CascadeSchema` | declaration point: properties, facts+reducers, flags; auto-indices; seals on engine bind |
| `CascadeProperty<T>` | typed property: committed/staged columns, change policy, typed mutation output |
| `CascadePropertyKey` | untyped base handle (mutation drain, aggregate checks) |
| `CascadeFact<TPayload>` | typed fact kind: preallocated payload queue + its single reducer |
| `CascadeReducer<TPayload>` | reducer delegate: `(context, payload)` |
| `CascadeValueEquality<T>` | per-property change policy delegate |
| `CascadeSignal` | empty payload for facts/markers that carry no data |
| `CascadeEngine` | sealed tick runner: queue facts, reduce, commit, publish |
| `CascadeReducerContext` | bound-entity API: `Read`, `HasFlag`, `Stage`, `StageIfPriorityAtLeast`, `Produce`, `DestroyEntity` |
| `CascadePropertyMutation` | untyped (entity, property) record in commit order |
| `CascadeMutationHandler<T>` | typed consumer callback: `(entity, previous, next)` |
| `CascadeEntityFlagKey` | schema-declared flag with auto-assigned bit (0-511) |
| `CascadeTickCounters` | produced/processed facts, reducer runs, mutations, touched, skipped |
| `Bitmask512` | 512-bit mask backing entity flags |

Internal plumbing (not part of the host API): `CascadeEntityStore`,
`CascadeFactQueue`, `CascadeFactRef`, `CascadeMutationLog`,
`CascadeTouchedEntitySet`.

Not core: ammo, movement, cues, UI/audio/network dispatch, Unity object
binding. Those live in the host schema, facade, and after-passes
(see `Assets/HestiaGame`).

## Why a Commit Phase Exists

The two-phase state (staged -> committed) is the engine's core guarantee, not
incidental complexity:

```text
order independence   reducers read committed, write staged; order cannot leak
atomicity            a failed tick discards staged work; committed state stays coherent
change detection     commit is the single place "did it actually change?" is answered
publish boundary     typed mutations exist because commit compared previous vs next
```

Remove the phase and reducers write live state in queue order - that is the
ECS hidden-order problem this package exists to delete.

What was removed is the per-property commit-function ceremony of the first
MVP: hand-registered committers, a per-tick preflight scan, and identical
`CommitStagedIfChanged()` one-liners. Commit policy is now data declared on
the property (default equality / custom equality / always-publish marker),
which cannot throw mid-commit and needs zero code in the common case.

## Memory and Allocation Model

```text
declaration time     all columns and queues preallocated from entityCapacity and fact capacities
hot path             Stage/Read/Produce/EnqueueFact/RunTick: zero allocations, no boxing
storage              struct-of-arrays per property; memory scales with declared properties
mutation log         amortized array (doubles on first growth, then allocation-free)
```

Memory per property is `entityCapacity * sizeof(T) * 4` (committed, staged,
previous, next) plus two int columns - pay only for what the schema declares,
not for a 512-slot universe per entity.

## Performance Model

Target cost per tick:

```text
O(queued facts + reducer runs + staged writes + mutated properties)
```

Counters exposed via `LastCounters` after every successful tick:

```text
ProducedFacts, ProcessedFacts, ReducerRuns, MutatedProperties,
SkippedNonRelevant, RegisteredReducers, TouchedEntities
```

Cascade is worse than a clean sparse-set ECS when facts are overproduced; the
counters exist so that failure is measurable on day one, not discovered in a
profiler. Cascade is better than ECS specifically when many systems contend
for the same property, when execution order became gameplay, when temporal requests miss current loop or when
debugging needs "which fact changed which property?".

## Known Limitations (accepted for now)

```text
fixed entity capacity        capacity is set at schema construction; no growth
no id recycling              destroy is permanent; hosts own id allocation
one reducer per fact kind    fan-out is modeled by producing more facts
schema:engine is 1:1         one world per schema instance; create pairs as needed
WasPropertyMutated(entity)   linear scan over that property's mutations (small per tick)
no query/iteration API       hosts iterate their own entity lists; add only when proven necessary
```

## Red Flags

```text
reducer writes committed state directly (SetCommitted is host-only initialization)
fact or property keys created outside the schema
non-relevant entities still producing projection-only facts
fact production loop without guard or diagnostics
one-shot cue as a raw event instead of a marker property mutation
core package tracking consumers (UI/audio/network routes)
change policies hidden outside the schema declaration
foreach-entity foreach-reducer scanning ("did anything change?")
```

## Sample and Tests

The executable proof lives outside the package:

- Schema + facade: [Assets/HestiaGame](../HestiaGame)
- Tests: [Assets/Tests/HestiaGameCascadeTests.cs](../Tests/HestiaGameCascadeTests.cs)

The tests verify: ammo current/empty mutate independently; empty transition
publishes a cue marker; typed mutation output carries previous/next values;
priority resolves same-property conflicts; epsilon policy swallows noise;
non-relevant cue work produces zero facts; a 1000-entity world with one input
runs one reducer; duplicate facts run two reducers but commit once; schemas
reject duplicate names and seal on engine construction; foreign-schema keys
are rejected; reducer fact cycles and failed reducers abort staged work
atomically; destroyed entities are cleared without stopping the tick.
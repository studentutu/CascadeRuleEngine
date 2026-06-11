# Cascade Rule Engine

## Purpose

Cascade is a small fact-reduction pipeline. Its job is not to own UI, audio,
replication, or host after-pass dispatch. Its job is to take facts, run explicit
reducers, stage entity-local state, commit touched entities, and report which
entity properties mutated.

```text
input/events
  -> facts
  -> reducer functions
  -> entity staged state
  -> commit touched entities
  -> mutated entity-property pairs
```

That is the package boundary. Project code can use the mutation output to drive
UI, audio, networking, ECS bridges, or any other after-pass, but Cascade core
does not track or schedule those systems.

## Core Rules

```text
Reducers do not mutate committed state.
Reducers receive context + fact.
Reducers stage state inside the bound entity or produce more facts.
Entities commit staged state once per tick.
Commit functions decide whether staged values become committed values.
Committed changes record mutated entity-property pairs.
```

This replaces ECS-style hidden execution order:

```text
old ECS:
  many systems write the same component in hidden order

cascade:
  facts map to reducer functions explicitly
  reducers stage changes on one entity context
  one entity commit applies staged values once
  mutation output is explicit and inspectable
```

## Minimal Terms

### Entity State

Committed and staged gameplay data for one dense entity id.

Examples:

```text
Entity_17.Resources.Ammo.Current
Entity_17.Resources.Stamina.Band
Entity_17.Mobility.Position
Entity_17.Mobility.Grounded
```

### Fact

A tick-local statement that wants reducer work.

```text
FactKey
TargetProperty
EntityId
Priority, optional and property-local
Payload
```

Examples:

```text
AmmoSpendRequested(Entity_17, amount: 1)
DesiredPosition(Entity_17, value)
FootstepCue(Entity_17)
DryFireCue(Entity_17)
```

### Reducer

A function registered by `FactKey`.

Reducers read:

```text
committed entity state
the current fact payload
tick-local facts through explicit context APIs
```

Reducers write:

```text
new facts
entity-local staged state
```

Reducers do not call Unity objects, UI, audio, networking, or after-pass code.

### Property Commit Function

A function registered by `CascadePropertyKey`.

Commit functions are the only place staged values become committed values. They
own per-property rules like exact compare, epsilon compare, priority acceptance,
or marker-style mutation output.

### Property Mutation

The only core output after a successful tick.

```text
CascadePropertyMutation(EntityId, Property)
```

The mutation list is deduplicated per entity-property pair and stays available
until the next `RunTick` or explicit `ClearMutations`.

## Reduction Loop

```text
facts = facts produced by input/events

while unprocessed fact exists:
  fact = next fact
  reducer = ReducerMap[fact.Kind]
  context = entity + fact buffer + touched set
  reducer(context, fact)
  reducer may stage state inside entity
  reducer may append more facts

commit touched entities once
record mutated entity-property pairs
clear tick-local facts and staged state
```

Reducer order must not matter because reducers write only staged state or append
facts. Committed state changes only during commit.

Required guards:

```text
max_reducer_runs_per_tick
max_facts_per_tick
cycle diagnostics for fact production
```

Guard failure policy:

```text
stop tick
do not commit partial staged state
clear tick-local state
surface the error
```

No silent partial commits.

## Commit

Commit is the only place staged entity state becomes committed state.

```text
for each touched entity:
  validate every staged property has a commit function
  for each staged property:
    run PropertyCommitMap[property]
    if committed value changed:
      record CascadePropertyMutation(entity, property)
```

Committer examples:

```text
single owner:
  accept the staged value if changed

float epsilon:
  accept only if abs(previous - next) exceeds the property epsilon

marker/cue:
  record a mutation even if the committed marker value is unchanged
```

Priority belongs to the property policy, not to a project-wide enum.

## Relevance

Relevance is a project decision before fact production.

```text
if work changes authoritative truth:
  enqueue the fact
else if entity is relevant to the local projection:
  enqueue the fact
else:
  skip before producing a fact
```

Core tracks skipped input with counters, but it does not own visibility,
interest management, UI routing, or network fanout.

Performance target:

```text
O(input/events + processed facts + touched entities + mutated properties)
```

Failure cases:

```text
non-relevant entities still producing projection-only facts
unbounded fact production loop
multiple systems writing Position directly
commit policies hidden outside the property table
after-pass routing moved back into the core package
```

Required counters:

```text
produced_facts
processed_facts
reducer_runs
registered_reducers
mutated_properties
skipped_non_relevant
touched_entities
allocations_bytes, when measured by the host project
```

## Reusable Minimal Core

Keep the reusable engine core small. If a class mentions ammo, movement, HUD,
audio, weapons, or character state, it is not core.

The reusable folder boundary is:

```text
Assets/CascadeEngine
```

That folder must be liftable into another Unity project without copying the
Hestia sample. `Assets/HestiaGame` is project/domain code that proves the rules
and is allowed to mention ammo, movement, HUD, and cues.

Core pieces:

```text
CascadeEntityId              typed dense entity id
CascadeEntityFlagKey         typed bit-addressable entity filter flag key
CascadeFactKey               typed fact kind
CascadePropertyKey           typed target property key
CascadeValue                 generic value wrapper for facts, staged state, and committed state
CascadeFact                  immutable tick-local statement
CascadeFactBuffer            preallocated fact storage
CascadeEngine<TContext>      core tick runner for facts, commits, mutation output, and cleanup
CascadeEntityState           core committed/staged property slots
CascadeEntityStateStore      dense core entity state store
CascadeReducerContext        core reducer context for fact production and staging
CascadeReducerMap<TContext>  explicit FactKey -> reducer function table
CascadePropertyCommitMap     explicit PropertyKey -> commit function table
CascadePropertyCommitContext context passed to property commit functions
CascadePropertyMutation      committed entity-property mutation output
CascadePropertyMutationSet   deduplicated mutation output list
CascadeTouchedEntitySet      touched entity list for commit/cleanup
CascadeTickCounters          instrumentation and budget assertions
Bitmask512                   reusable 512-bit mask for flags and fixed key checks
```

Not core:

```text
ammo state
movement state
audio cue state
feature reducers
feature commit policies
UI dispatch
audio dispatch
network replication
Unity object binding
feature entity property bags
```

Those live in a project schema, facade, or after-pass.

## Hestia Sample Code

The executable Hestia sample proves the core shape without pretending to be the
final framework. Domain names are declared in one schema file.

See [HestiaGame folder](../HestiaGame).
Tests live under [Package and domain tests](../Tests/HestiaGameCascadeTests.cs).

Ammo shape:

```text
InputFireWeapon(entity)
  -> AmmoSpendRequested fact(payload: amount=1)
  -> ReducerMap[AmmoSpendRequested]
  -> ReduceAmmoSpendRequested(context, fact)
  -> reducer checks entity flag AcceptsAmmoInput
  -> context stages AmmoCurrent and AmmoEmpty properties in core entity state
  -> optional DryFireCue fact
  -> commit touched entity once
  -> PropertyCommitMap[AmmoCurrent] commits AmmoCurrent if changed
  -> PropertyCommitMap[AmmoEmpty] commits AmmoEmpty if changed
  -> mutation output contains exact changed entity-property pairs
```

Movement shape:

```text
InputMove(entity, desired)
  -> DesiredPosition fact(payload: desired)
  -> ReducerMap[DesiredPosition]
  -> context stages Position property in core entity state
  -> commit touched entity once
  -> PropertyCommitMap[Position] commits Position if changed
  -> mutation output contains (entity, Position)
```

What this proves:

```text
input creates facts with payload first
fact kind maps explicitly to a reducer function
reducers receive context + fact
core entity state owns staged values
core commit applies touched entities once
property keys map explicitly to commit functions
ammo spend mutates AmmoCurrent while ammo remains non-empty
empty transition mutates AmmoCurrent, AmmoEmpty, and PublishedAudioCues
Position has one owner
buffers are reused after construction
```

What this does not include:

```text
generic property policies
host after-pass registration
dirty after-pass queues
Unity object routing
```

Those are host-project after-passes, not core package primitives.

## Reality Check

This is realistic if the engine keeps these constraints:

```text
reducers do not scan all entities
facts are stored in preallocated buffers
fact keys resolve through an explicit reducer map
commit touches only entities with staged properties
destroyed touched entities are cleared and skipped before commit
destroyed entities are omitted from reducer passes
after-pass routing reads mutation output instead of being owned by core
non-relevant projection work is skipped before facts are produced
```

This is not realistic if it becomes:

```text
foreach entity
  foreach reducer
    check if something changed

or:

fact produced
  -> event bus
  -> arbitrary listeners
  -> more state writes

or:

mutated property
  -> core-owned UI/audio/network routing
```

Hestia usage verifies:

```text
Ammo spend mutates current ammo but not empty while ammo remains non-empty.
Empty transition mutates current ammo, empty state, and audio cue marker.
Movement mutates only Position.
Non-relevant cue work produces no facts and no mutations.
A 1000-entity world with one ammo input runs one reducer.
Two fire facts run two reducer functions and still commit once.
Unknown property committers fail before silent commit.
Destroyed entities with staged work are cleared and do not stop the tick.
```

## Compared To Cached Sparse-Set ECS

Sparse-set ECS with cached queries is already good when:

```text
systems are few
component ownership is clean
query membership changes are cheap
systems do not fight over the same output
```

Cascade is better only for these failure modes:

```text
many systems want to affect the same property
execution order became gameplay
non-relevant projection work should not exist
debugging needs "which fact changed which property?"
temporal gameplay changes should resolve in one cascade tick
```

Cascade cost:

```text
input/event creates fact with payload for a target entity
ReducerMap resolves fact kind to one reducer function
reducer stages properties through the context
PropertyCommitMap resolves each staged property to one commit function
core commit applies touched entities once
mutation output exposes exact changed entity-property pairs
```

Performance can be worse than ECS if facts are overproduced. The design is
useful because the counters make that failure measurable early.

## Test Cases

### Ammo

```text
FireWeapon
  -> AmmoSpendRequested fact(payload: amount)
  -> ReduceAmmoSpendRequested(context, fact)
  -> context stages AmmoCurrent
  -> optional context stages AmmoEmpty
  -> optional DryFireCue fact if ammo crossed empty
  -> ReduceAudioCue(context, fact)
  -> commit touched entity
  -> mutation output reports changed properties
```

Pass:

```text
Ammo spend does not mutate movement.
Ammo spend does not mutate AmmoEmpty while ammo remains non-empty.
Empty transition mutates AmmoEmpty.
Audio cue marker can mutate on repeated cue ticks.
```

### Movement

```text
InputMove
  -> DesiredPosition fact(payload: target)
  -> ReduceDesiredPosition(context, fact)
  -> context stages Position
  -> PropertyCommitMap[Position]
  -> commit touched entity
  -> mutation output reports Position when changed
```

Pass:

```text
Gravity/Collision/Physics/Inertia/PID never write Position directly.
MovementSolverReducer is the only final movement owner.
Position mutations are exact and inspectable.
```

### Fact Loop

```text
Input
  -> fact_1
  -> reducer_A(context, fact_1)
  -> fact_2
  -> reducer_B(context, fact_2)
  -> fact_3
  -> no more unprocessed facts
  -> commit touched entities
  -> mutations
```

Pass:

```text
Reducer order does not affect committed state.
Touched entities are committed once.
Cycle guard catches infinite fact loops.
Cycle diagnostics show responsible fact types.
```

## Red Flags

```text
reducer writes committed state directly
feature declares fact mappings outside the canonical domain table
global priority bands return
generic Add/Multiply/Override/enum primitives become the main stacking model
non-relevant entities still produce projection-only facts
Gravity/Collision/Physics/Inertia/PID writes Position or the same fact directly
fact production loop has no guard or diagnostics
one-shot cue is a raw event instead of explicit publishable change (state, marker mutation or some form of publish-queue)
core package starts tracking consumer (UI/audio/network routes again)
```

## First Slice

```text
ammo current/empty
movement staged position
one footstep cue marker
one dry-fire cue marker
canonical fact table
mutation output after commit
```

Success:

```text
non-relevant cue work skipped
ammo current and empty mutate independently
movement position has one owner
mutated properties are enough output for host after-passes
fact loop diagnostics visible
zero hot-path allocations after warmup
```

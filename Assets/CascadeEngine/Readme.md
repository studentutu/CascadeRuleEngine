# Cascade Rule Engine

## Purpose

Need a good minimal and coherent package core, so that we can pick one folder and drop into any other project ready to be used.

Take inspiration from Virtual-DOM and React for the minimal and coherent package core and good set of primitives.

What it really is about:

Replace ECS-style hidden execution order with a small fact pipeline.

```text
input/events -> facts -> reducer functions -> entity staged state -> commit touched entities -> dirty consumers
```

The engine should be easy to explain:

```text
Reducers do not mutate published state.
Reducers receive context + fact.
Reducers stage state inside the target entity or produce more facts.
Entities commit staged state once.
Committed property changes mark exact entity-scoped consumers dirty.
```

This is the generational improvement over ECS:

```text
old ECS:
  many systems write the same component in hidden order

cascade:
  facts map to reducer functions explicitly
  reducers stage changes on one entity context
  one entity commit publishes staged values once
  consumers react only to dirty properties
```

### Usability

We need a good set of primitives and clear and rigid pipeline and intuitive usage:

1. Simple enough to understand and walk through the any reducer/consumer/properties/fact and clean mutation of the state.
2. Extendable to add custom reducers/consumers.
3. We need all major feature parity to ecs: entity and per entity state, queryable entity state from reducers/consumers, zero-allocation in the hot path, performant for 500+ entities.
4. Easy enough to drop cascade package in and start using instead of ecs:

```text
old ECS system -> input/event -> Fact
-> cascade engine Tick
-> published property -> old ECS/world/unity-ui consumer
```

---

## Core Pipeline

This is the core concept and core package.

```text
Tick
  -> input/events create facts with payloads
  -> process unhandled facts through ReducerMap
  -> reducers stage state inside entities or append more facts
  -> commit touched entities once (same as mark changed properties dirty and apply mutation)
  -> mark exact properties/published slices dirty (part of the commit)
  -> run only consumers mapped to dirty properties for the affected entities
```

Compact form:

```text
Input
  -> fact_1
  -> ReducerMap[fact_1] -> reducer_A(context, fact_1)
  -> fact_2
  -> ReducerMap[fact_2] -> reducer_B(context, fact_2)
  -> fact_3
  -> no more unprocessed facts
  -> commit touched entities
  -> dirty consumers
```

Everything else is extension and usage on top of it.

---

## Minimal Terms

### Entity State

Committed gameplay data.

Examples:

```text
Entity_17.Resources.Ammo.Current
Entity_17.Resources.Stamina.Band
Entity_17.Mobility.Position
Entity_17.Mobility.Grounded
Entity_17.Computed.MovementPermissions
Player_12.Entity_17.Published.Hud.AmmoText
```

### Dirty Reducer

A reducer scheduled because input, events, or facts changed something it cares about.

Reducers read:

```text
committed state
current tick facts
request/cue entities
```

Reducers write:

```text
facts
entity-local staged state
```

Reducers do not write final state. Reducers do not call consumers.

### Input Fact

The first item in the reduction pipeline is a normal fact with payload.

```text
input/event
  -> Fact(EntityId, FactKey, TargetProperty, Payload)
  -> ReducerMap resolves FactKey to reducer function
```

This keeps input handling out of reducers. Input does not call reducers directly and does not mutate final state.

### Fact

A tick-local statement that wants to affect a state property or another reducer.

```text
FactKey
TargetProperty
EntityId
SourceEntityId
Priority, optional and property-local
Payload
```

Examples:

```text
AmmoSpendRequested(Entity_17, amount: 1)
DesiredPosition(Entity_17, value)
FootstepCue(Player_12, CueEntity_9001)
DryFireCue(Player_12)
MovementPermissionInput(Entity_17, CanSprint=true)
```

### Target Property

The exact property a fact wants to affect.

```text
Entity_17.Resources.Ammo.Current
Entity_17.Computed.MovementPermissions
Player_12.Entity_17.Published.Hud.AmmoIcon
```

All conflict resolution happens per target property.

### Priority

No global priority bands.

Priority exists only when several facts target the same property in the same commit. The target property owns the priority comparison.

Bad:

```text
one global priority scale shared by every property
feature priority numbers that mean different things for different properties
```

Good:

```text
Property: Entity.Mobility.Position
Policy: ServerCorrection > MovementSolverPrediction

Property: Entity.Computed.MovementPermissions
Policy: DeathLock > StunLock > RootLock > InputIntent

Property: Player.Published.Hud.AmmoIcon
Policy: entity-local staged value wins unless this property explicitly supports priority
```

Most properties should not need priority. They should have one owner fact type. Add priority only where multiple facts can legitimately target the same property.

---

## Reduction Loop

The loop is simple:

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
```

Reducer order must not matter because reducers write only entity-local staged state or append facts. Published state changes only during commit.

Required guards:

```text
max_reduction_rounds
max_facts_per_tick
max_facts_per_entity
max_fact_rewrites_per_property
cycle_trace
```

Guard failure policy:

```text
authoritative truth: stop tick, no commit
player-scoped projection: use previous committed state or continue next tick
debug/presentation-only cue: drop
```

No silent partial commits.

---

## Commit

Commit is the only place staged entity state becomes published state.

```text
for each touched entity:
  resolve staged values already held by that entity
  write committed value if changed
mark changed properties dirty
mark mapped published slices dirty
queue mapped consumers
```

Property policy examples:

```text
single owner:
  accept the only fact

latest:
  accept latest tick-local fact

highest priority wins:
  sort by property-local priority
  accept first

ordered fold:
  sort by property-local priority
  apply fold from highest to lowest

set union:
  merge all unique values

min/max:
  choose min/max
```

Priority belongs to the property policy, not to a project-wide enum.

---

## Dirty Consumers

Consumers react because committed properties are dirty.

```text
fact commit changed property
  -> DirtyPropertyKey
  -> DirtyPublishedSlice, if property is published
  -> FieldMask
  -> Version++
  -> mapped (EntityId, ConsumerKey) queued
```

Runtime data:

```text
DirtyPropertyMask[entityId]
DirtyPublishedMask[playerContextId][entityId]
DirtyFieldMask[playerContextId][entityId][sliceId]
PublishedVersion[playerContextId][entityId][sliceId]
ConsumerQueue
ConsumerQueueItem(EntityId, ConsumerKey)
```

Consumer map:

```text
Published.Hud.AmmoText
  -> HudAmmoTextConsumer

Published.Hud.AmmoIcon
  -> HudAmmoIconConsumer

Published.CharacterMotor
  -> CharacterMotorConsumer

Published.AudioCues
  -> AudioCueConsumer

Published.VfxCues
  -> VfxCueConsumer

Published.Replication.Transform
  -> ReplicationConsumer
```

Consumers receive the affected entity id and read committed published state from that entity.
No consumer reads facts.

---

## Canonical Fact Table

Keep one human-authored table.
One place to see all facts and their reducers.

```text
FactKey
  -> reducer function
  -> target properties
  -> dirty published slices
  -> consumers
```

Do not scatter declarations across feature files. This can be done per-project or even per context, but we need to concentrate and actual usage example.

Example:

```text
AmmoSpendRequested:
  reducer function:
    ReduceAmmoSpendRequested
  target properties:
    Entity.Resources.Ammo.Current
    Player.Published.Hud.AmmoText
  dirty published slices:
    Published.Hud.AmmoText
  consumers:
    HudAmmoTextConsumer

DryFireCue:
  reducer function:
    ReduceAudioCue
  target properties:
    Published.AudioCues
  consumers:
    AudioCueConsumer

DesiredPosition:
  reducer function:
    ReduceDesiredPosition
  target properties:
    Entity.Mobility.Position
    Entity.VisibleSnapshot.Position
    Player.Published.VisibleSnapshot.Position
    Player.Published.Replication.Transform
  consumers:
    CharacterMotorConsumer
    ReplicationConsumer
```

Validation:

```text
every FactKey has one row
unknown FactKey fails
duplicate consumer in a row fails
duplicate dirty slice in a row fails
cycles are traced
fanout is counted
```

---

## Relevance

This is built in performance/budgeting stage.
Use one relevance condition:

```text
if work changes authoritative truth:
  run it
else if entity is relevant to player context:
  run it
else:
  do not produce facts
  do not queue consumers
```

Predicted player is always first.
Nearby/relevant players follow.
Future replacement can use occlusion/PVS.

Performance target:

```text
O(input/events + processed facts + touched entities + dirty properties + queued consumers)
```

Needed cases:

```text
consumer should be able to poll source state
reduction should be able to query/add facts to multiple entities
entity can be created at runtime or destroyed
```

Failure cases:

```text
non-relevant entities still producing facts
giant HudDirty or MovementDirty flag
unbounded fact production loop
multiple systems writing Position
culling input fact impossible to implement cleanly via reductions
```

Required counters:

```text
produced_facts
processed_facts
reducer_runs
registered_reducers
fact_rewrites_per_property
dirty_properties
dirty_published_slices
queued_consumers
skipped_non_relevant
allocations_bytes
```

---

## Real usage example

Small team have game with over 1500 systems and 500 components with scattered state/hidden execution flow and frequent clean-ups before critical use which constantly breaks gameplay feature.

We need alternative to ECS.

### Movement Case

Current writers:

```text
Gravity
Collision
Physics
Inertia
ActualPIDMovementController
```

They all affect `Position`. Do not let them all write `Position`.

Correct pipeline:

```text
InputMove
  -> DesiredPosition fact(payload: target)
  -> ReducerMap[DesiredPosition]
  -> ReduceDesiredPosition(context, fact)

Movement reducer context reads:
  Gravity facts
  Collision facts
  Physics query facts
  Inertia facts
  PID facts
  committed movement state

Movement reducer stages on entity:
  Position
  Velocity
  Grounded
```

Commit:

```text
entity staged Position
  -> Entity.Mobility.Position
  -> Entity.VisibleSnapshot.Position
  -> Player.Published.VisibleSnapshot.Position
  -> Player.Published.Replication.Transform
  -> CharacterMotorConsumer
  -> ReplicationConsumer
```

Only `MovementSolverReducer` owns final movement facts. Unity physics can be a query service or a consumer, not a second gameplay owner of `Position`.

---

### Request And Cue Entities

One-shot requests and cues are entities.

```text
DamageRequestEntity
TrapTriggerRequestEntity
FootstepCueEntity
WeaponFireCueEntity
DryFireCueEntity
DeathCueEntity
```

Fields:

```text
EntityId
SubjectEntityId
SourceEntityId
FactKey
PayloadId
ExpireTick
PredictionKey
```

Non-relevant players do not project cue entities.

```text
Movement step
  -> FootstepCueEntity
  -> relevant Player_12 gets Published.AudioCues dirty
  -> non-relevant Player_88 gets nothing
```

---

### Reusable Minimal Core

Keep the reusable engine core small. If a class mentions ammo, movement, HUD,
audio, weapons, or character state, it is not core.

The reusable folder boundary is:

```text
Runtime/CascadeEngine/Core
```

That folder must be liftable into another Unity project without copying the
Hestia sample. `Assets/HestiaGame` is project/domain code that
proves the rules and is allowed to mention ammo, movement, HUD, and cues.

Core pieces:

```text
CascadeEntityId             typed dense entity id
CascadeConsumerKey          typed bit-addressable consumer key
CascadeEntityFlagKey        typed bit-addressable entity filter flag key
CascadeFactKey              typed fact kind
CascadePropertyKey          typed target property key
ReducerPayload              generic payload wrapper carried by facts
CascadeFact                 immutable tick-local statement
CascadeFactBuffer           preallocated fact storage
CascadeEngine<TContext>     core tick runner for facts, commits, dirty consumers, and cleanup
CascadeEntityState          core committed/staged property slots
CascadeEntityStateStore     dense core entity state store
CascadeReducerContext       core reducer context for fact production and staging
CascadeReducerMap<TContext> explicit FactKey -> reducer function table
CascadePropertyCommitMap    explicit PropertyKey -> commit function table
CascadePropertyCommitContext context passed to property commit functions
Bitmask512                  reusable 512-bit mask for dirty/registered keys
CascadeDirtyConsumerSet     entity-scoped dirty consumer work backed by per-entity masks
CascadeTouchedEntitySet     touched entity list for commit/cleanup
CascadeTickCounters         instrumentation and budget assertions
```

Not core:

```text
ammo state
movement state
audio cue state
feature reducers
feature commit policies
feature consumer mappings
feature entity property bags
```

Those live in a project schema and a project facade.

### Hestia Sample Code

The executable Hestia sample proves the core shape without pretending to be the
final framework. Domain names are declared in one schema file.

See [HestiaGame folder](Assets/HestiaGame).
To test it use tests under [Package and domain tests](Assets/Tests/HestiaGameCascadeTests.cs)

Fact-to-commit shape:

```text
InputFireWeapon(player)
  -> AmmoSpendRequested fact(payload: amount=1)
  -> ReducerMap[AmmoSpendRequested]
  -> ReduceAmmoSpendRequested(context, fact)
  -> reducer checks entity flag AcceptsAmmoInput
  -> context stages AmmoCurrent and AmmoEmpty properties in core entity state
  -> optional DryFireCue fact
  -> PropertyCommitMap[AmmoCurrent] -> HudAmmoText dirty for player if value changed and PublishesHudAmmo is set
  -> PropertyCommitMap[AmmoEmpty] -> HudAmmoIcon dirty for player if value changed and PublishesHudAmmo is set
  -> commit touched entity once
  -> exact entity-scoped dirty consumers
```

Movement shape:

```text
InputMove(player, desired)
  -> DesiredPosition fact(payload: desired)
  -> ReducerMap[DesiredPosition]
  -> context stages Position property in core entity state
  -> PropertyCommitMap[Position] -> CharacterMotor dirty if committed value changed
  -> commit touched entity once
  -> CharacterMotor dirty only if Position changed
```

What this proves:

```text
input creates fact with payload first
fact kind maps explicitly to a reducer function
reducers receive context + fact
core entity state owns staged values
core commit applies touched entities once
property keys map explicitly to commit functions
Bitmask512 backs dirty consumer checks
ammo spend dirties ammo text, not ammo icon, while ammo remains non-empty
empty transition dirties ammo icon
Position has one owner
consumers queue from dirty committed properties
buffers are reused after construction
```

What this does not include:

```text
generic property policies
Unity object consumers
```

Those are next-step additions only after this slice is proven.

---

## Reality Check

This is realistic if the engine keeps these constraints:

```text
reducers do not scan all entities
facts are stored in preallocated buffers
fact keys resolve through an explicit reducer map
commit touches only entities with staged properties
destroyed touched entities are cleared and skipped before commit
destroyed touched entities are omitted from the reducer passes (ideally dirty entity is cleared/returned to pool/removed cleanly from the pipeline)
consumers are queued from dirty properties (avoid subscription based events)
non-relevant player-scoped work is skipped before facts are produced
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
```

Hestia usage verifies:

```text
Ammo spend dirties text but not icon while ammo remains non-empty.
Empty transition dirties icon and audio.
Movement Position dirties CharacterMotor only.
Non-relevant cue work produces no facts and no consumer work.
A 1000-entity world with one ammo input runs one reducer.
Two fire facts run two reducer functions and still commit once.
Unknown property committers fail before silent publish.
Destroyed entities with staged work are cleared and do not stop the tick.
```

### Compared To Cached Sparse-Set ECS

Sparse-set ECS with cached queries is already good when:

```text
systems are few
component ownership is clean
query membership changes are cheap
systems do not fight over the same output
change/request components/markers are not cleared until used by the target system (which inherently leaks)
there is a strict temporal state for entity (meaning, state resolution may happen in a few ticks) 
```

Cascade is better only for the failure modes:

```text
many systems want to affect the same property
execution order became gameplay
presentation consumes too much broad state
non-relevant player work should not exist (instead of adding to all systems, skip is part of the pipeline)
debugging needs "why did this property change?"
all temporal gameplay changes become always become single cascade resolution tick (no more wait next tick to do x)
```

Cached ECS query cost:

```text
system wakes
query gives matching entities
system checks/updates component state
multiple systems may write same component
consumer still poll broad state
adding new "core" component suddenly needs to update hundreds of systems and will break if at least one place is missing (example Potential-Visible-Set in addition to the Culling-Visibility)
initialization of system is not free and often spikes on all entities
```

Cascade cost:

```text
input/event creates fact with payload for target entity or broadly
ReducerMap resolves fact kind to one reducer function (can generate next fact on a single or multiple entities)
reducer stages properties through the context
PropertyCommitMap resolves each staged property to one commit function
core commit publishes touched entities once
dirty property queues exact entity-scoped consumers
```

Performance can be worse than ECS if facts are overproduced (reduction passes must be minimized).
Performance is better only if the fact table and relevance gates keep fanout small.

Required benchmark:

```text
Cascade baseline:
  reducer functions executed
  skipped non-relevant reduction
  facts processed
  facts produced
  touched entities committed
  consumers queued
  skipped non-relevant work
```

If Cascade runs more reducers/facts than ECS visits entities, it loses. The design is useful because it makes that failure measurable early.

---

## Test Cases

### Ammo

```text
FireWeapon
  -> AmmoSpendRequested fact(payload: amount)
  -> ReduceAmmoSpendRequested(context, fact)
  -> context stages Ammo.Current
  -> PropertyCommitMap[Ammo.Current]
  -> commit touched entity
  -> dirty Published.Hud.AmmoText
  -> HudAmmoTextConsumer

if ammo crossed empty/non-empty:
  -> context stages Ammo.Empty
  -> PropertyCommitMap[Ammo.Empty]
  -> DryFireCue fact if empty
  -> ReduceAudioCue(context, fact)
  -> dirty Published.Hud.AmmoIcon
  -> HudAmmoIconConsumer
```

Pass:

```text
Ammo spend does not dirty movement.
Ammo spend does not dirty ammo icon while ammo remains non-empty.
Empty transition dirties ammo icon.
Ammo icon consumer is not queued every shot.
```

### Movement

```text
InputMove
  -> DesiredPosition fact(payload: target)
  -> ReduceDesiredPosition(context, fact)
  -> context stages Position
  -> PropertyCommitMap[Position]
  -> commit touched entity
  -> dirty Published.VisibleSnapshot.Position
  -> CharacterMotorConsumer
```

Pass:

```text
Gravity/Collision/Physics/Inertia/PID never write Position directly.
MovementSolverReducer is the only final movement owner.
Position dirties only movement/visibility/replication consumers.
```

### Death

```text
DamageRequestEntity
  -> DamageRequested fact
  -> ReduceDamageRequested(context, fact)
  -> context stages HealthValue
  -> Death fact
  -> ReduceMovementPermissions(context, fact)
  -> context stages MovementPermissions
  -> DeathCueEntity
  -> commit touched entity
  -> CharacterMotorConsumer
  -> AudioCueConsumer if relevant
  -> VfxCueConsumer if relevant
```

Pass:

```text
Death truth runs even when source is occluded.
Death VFX/audio only project to relevant players.
No consumer reads partial health state.
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
```

Pass:

```text
Reducer order does not affect committed state.
Touched entities are committed once.
Cycle guard catches infinite fact loops.
Cycle trace shows responsible fact types and reducers.
```

---

## Selling Points

```text
Feature impact is visible in one fact table.
Reducers do not mutate final state.
Facts resolve same-property conflicts explicitly.
State commits once.
Consumers react only to exact dirty properties.
Non-relevant work is not produced.
Movement has one final owner.
One-shot cues stay ECS-friendly as entities.
Debugging becomes: which fact dirtied which property and consumer?
```

Pitch:

```text
ECS let every feature write shared state.
Cascade makes features produce facts.
Commit resolves facts once per property.
Dirty properties wake exact entity-scoped consumers.
```

---

## Red Flags

```text
reducer writes final state directly
feature declares fact mappings outside the canonical domain table
global priority bands return
generic Add/Multiply/Override/enum primitives becomes the main stacking model
non-relevant entities still produce player-scoped facts
giant HudDirty or MovementDirty flag appears
Gravity/Collision/Physics/Inertia/PID writes Position directly
fact production loop has no guard or cycle trace
one-shot cue is a raw event instead of an entity
```

---

## First Slice

```text
ammo text/icon
stamina sprint
movement solver staged position
one footstep cue entity
one dry-fire cue entity
canonical fact table validator
dirty property -> consumer dispatch
```

Success:

```text
predicted player processed first
non-relevant cue work skipped
ammo text and ammo icon dirty independently
movement position has one owner
consumers queue from dirty properties only
fact loop diagnostics visible
zero hot-path allocations after warmup
```

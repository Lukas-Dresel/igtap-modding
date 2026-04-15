using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace IGTAPReplay
{
    /// <summary>
    /// Reflection accessors for Movement private fields needed by the replay system.
    /// Extends the core mod's GameState with additional fields for snapshot/restore.
    /// </summary>
    public static class ReplayState
    {
        // Cached list of all instance fields on Movement, for exhaustive state dumping
        private static FieldInfo[] _allMovementFields;
        public static FieldInfo[] AllMovementFields
        {
            get
            {
                if (_allMovementFields == null)
                {
                    _allMovementFields = typeof(Movement).GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                return _allMovementFields;
            }
        }

        /// <summary>
        /// Dump every instance field on the Movement component with a short, parseable
        /// representation. For Rigidbody2D also dumps key physics properties.
        /// </summary>
        public static string DumpAllFields(Movement player)
        {
            if (player == null) return "(null player)";
            var sb = new StringBuilder();
            foreach (var f in AllMovementFields)
            {
                object v;
                try { v = f.GetValue(player); }
                catch (System.Exception ex) { v = $"<err:{ex.GetType().Name}>"; continue; }
                sb.Append(f.Name).Append('=').Append(FormatValue(v)).Append(' ');
            }
            // Also dump Rigidbody2D state
            var body = (Rigidbody2D)F_body.GetValue(player);
            if (body != null)
            {
                sb.Append("[RB ");
                sb.Append($"pos=({body.position.x:F3},{body.position.y:F3}) ");
                sb.Append($"vel=({body.linearVelocity.x:F3},{body.linearVelocity.y:F3}) ");
                sb.Append($"ang={body.angularVelocity:F3} ");
                sb.Append($"rot={body.rotation:F3} ");
                sb.Append($"sim={body.simulated} ");
                sb.Append($"sleep={body.IsSleeping()} ");
                sb.Append($"bodyType={body.bodyType} ");
                sb.Append($"grav={body.gravityScale}");
                sb.Append(']');
            }
            sb.Append($" [TR pos=({player.transform.position.x:F3},{player.transform.position.y:F3},{player.transform.position.z:F3})");
            sb.Append($" scale=({player.transform.localScale.x:F1},{player.transform.localScale.y:F1},{player.transform.localScale.z:F1})]");

            // Rigidbody2D contact points (what Box2D thinks the body is touching)
            if (body != null)
            {
                var contacts = new ContactPoint2D[16];
                int n = body.GetContacts(contacts);
                sb.Append($" [CT n={n}");
                for (int i = 0; i < n && i < 8; i++)
                {
                    var c = contacts[i];
                    sb.Append($" c{i}=(pt=({c.point.x:F2},{c.point.y:F2}),n=({c.normal.x:F2},{c.normal.y:F2}),sep={c.separation:F3})");
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        private static string FormatValue(object v)
        {
            if (v == null) return "null";
            if (v is float f) return f.ToString("F3");
            if (v is double d) return d.ToString("F3");
            if (v is Vector2 v2) return $"({v2.x:F3},{v2.y:F3})";
            if (v is Vector3 v3) return $"({v3.x:F3},{v3.y:F3},{v3.z:F3})";
            if (v is bool b) return b ? "T" : "F";
            if (v is int i) return i.ToString();
            if (v is System.Collections.IEnumerable && !(v is string))
            {
                // Skip long collections
                return "<coll>";
            }
            var t = v.GetType();
            if (!t.IsPrimitive && !t.IsEnum && t != typeof(string))
            {
                // For reference types, just show type name (avoid deep serialization)
                return t.Name;
            }
            return v.ToString();
        }

        // Physics
        public static readonly FieldInfo F_body = AccessTools.Field(typeof(Movement), "body");
        public static readonly FieldInfo F_momentum = AccessTools.Field(typeof(Movement), "momentum");
        public static readonly FieldInfo F_lastAppliedMomentum = AccessTools.Field(typeof(Movement), "lastAppliedMomentum");

        // Ground/wall state
        public static readonly FieldInfo F_onGround = AccessTools.Field(typeof(Movement), "onGround");
        public static readonly FieldInfo F_OnWall = AccessTools.Field(typeof(Movement), "OnWall");
        public static readonly FieldInfo F_coyoteFrames = AccessTools.Field(typeof(Movement), "coyoteFrames");
        public static readonly FieldInfo F_wallCoyoteFrames = AccessTools.Field(typeof(Movement), "wallCoyoteFrames");
        public static readonly FieldInfo F_wallCoyoteDir = AccessTools.Field(typeof(Movement), "wallCoyoteDir");

        // Ability resources
        public static readonly FieldInfo F_airDashesLeft = AccessTools.Field(typeof(Movement), "airDashesLeft");
        public static readonly FieldInfo F_airJumpsLeft = AccessTools.Field(typeof(Movement), "airJumpsLeft");
        public static readonly FieldInfo F_wallJumpsLeft = AccessTools.Field(typeof(Movement), "wallJumpsLeft");

        // Dash state
        public static readonly FieldInfo F_dashCooldown = AccessTools.Field(typeof(Movement), "dashCooldown");
        public static readonly FieldInfo F_dashActive = AccessTools.Field(typeof(Movement), "dashActive");
        public static readonly FieldInfo F_isDashingRight = AccessTools.Field(typeof(Movement), "isDashingRight");
        public static readonly FieldInfo F_dashBuffer = AccessTools.Field(typeof(Movement), "dashBuffer");
        public static readonly FieldInfo F_timeSinceLastDash = AccessTools.Field(typeof(Movement), "timeSinceLastDash");
        public static readonly FieldInfo F_dashSpeed = AccessTools.Field(typeof(Movement), "dashSpeed");

        // Jump state
        public static readonly FieldInfo F_jumpBuffer = AccessTools.Field(typeof(Movement), "jumpBuffer");
        public static readonly FieldInfo F_MostRecentUpwardsMoveWasJump = AccessTools.Field(typeof(Movement), "MostRecentUpwardsMoveWasJump");

        // Wall jump state
        public static readonly FieldInfo F_isFinalWallClingActive = AccessTools.Field(typeof(Movement), "isFinalWallClingActive");
        public static readonly FieldInfo F_finalWallClingTimeLeft = AccessTools.Field(typeof(Movement), "finalWallClingTimeLeft");

        // Air tracking
        public static readonly FieldInfo F_timeInAir = AccessTools.Field(typeof(Movement), "timeInAir");
        public static readonly FieldInfo F_heightOfLastJumpStart = AccessTools.Field(typeof(Movement), "heightOfLastJumpStart");

        // Input
        public static readonly FieldInfo F_xMoveAxis = AccessTools.Field(typeof(Movement), "xMoveAxis");
        public static readonly FieldInfo F_moveAction = AccessTools.Field(typeof(Movement), "moveAction");
        public static readonly FieldInfo F_jumpAction = AccessTools.Field(typeof(Movement), "jumpAction");
        public static readonly FieldInfo F_dashAction = AccessTools.Field(typeof(Movement), "dashAction");

        // Death
        public static readonly FieldInfo F_isDead = AccessTools.Field(typeof(Movement), "isDead");

        // Pause
        public static readonly FieldInfo F_pauseMenu = AccessTools.Field(typeof(Movement), "pauseMenu");

        /// <summary>
        /// A snapshot of all gameplay-relevant state needed to restore the player
        /// to an exact position for deterministic replay.
        /// </summary>
        public struct Snapshot
        {
            // Transform position (what Movement.Update reads for raycasts, and what
            // we write to player.transform.position on restore)
            public Vector2 Position;
            // Rigidbody2D position (can differ from transform.position after FixedUpdate
            // integrates velocity / resolves collisions without syncing transform).
            // This is captured separately and restored to body.position directly so the
            // body is at EXACTLY the right place it was during recording.
            public Vector2 BodyPosition;
            public Vector2 Velocity;
            public float AngularVelocity;
            public float BodyRotation;
            public Vector2 Momentum;
            public float LastAppliedMomentum;

            // Ground/wall
            public bool OnGround;
            public bool OnWall;
            public float CoyoteFrames;
            public float WallCoyoteFrames;
            public float WallCoyoteDir;

            // Ability resources
            public int AirDashesLeft;
            public int AirJumpsLeft;
            public int WallJumpsLeft;

            // Dash
            public float DashCooldown;
            public bool DashActive;
            public bool IsDashingRight;
            public bool DashBuffer;
            public float TimeSinceLastDash;

            // Jump
            public bool JumpBuffer;
            public bool MostRecentUpwardsMoveWasJump;

            // Wall
            public float WallJumpDirection;
            public float WallJumpMovementLockRemaining;
            public bool IsFinalWallClingActive;
            public float FinalWallClingTimeLeft;

            // Air
            public float TimeInAir;
            public float HeightOfLastJumpStart;

            // Input
            public float XMoveAxis;

            // Death
            public bool IsDead;

            // Facing
            public bool FacingRight;

            // Cutscene
            public Movement.cutsceneModes CutsceneMode;

            // Unlocks & limits (needed to verify replay is compatible)
            public bool DashUnlocked;
            public bool WallJumpUnlocked;
            public bool DoubleJumpUnlocked;
            public bool BlockSwapUnlocked;
            public int MaxAirDashes;
            public int MaxAirJumps;
            public int MaxWallJumps;

            // Respawn
            public Vector2 RespawnPoint;
        }

        public static Snapshot Capture(Movement player)
        {
            var body = (Rigidbody2D)F_body.GetValue(player);
            return new Snapshot
            {
                Position = (Vector2)player.transform.position,
                BodyPosition = body.position,
                Velocity = body.linearVelocity,
                AngularVelocity = body.angularVelocity,
                BodyRotation = body.rotation,
                Momentum = (Vector2)F_momentum.GetValue(player),
                LastAppliedMomentum = (float)F_lastAppliedMomentum.GetValue(player),

                OnGround = (bool)F_onGround.GetValue(player),
                OnWall = (bool)F_OnWall.GetValue(player),
                CoyoteFrames = (float)F_coyoteFrames.GetValue(player),
                WallCoyoteFrames = (float)F_wallCoyoteFrames.GetValue(player),
                WallCoyoteDir = (float)F_wallCoyoteDir.GetValue(player),

                AirDashesLeft = (int)F_airDashesLeft.GetValue(player),
                AirJumpsLeft = (int)F_airJumpsLeft.GetValue(player),
                WallJumpsLeft = (int)F_wallJumpsLeft.GetValue(player),

                DashCooldown = (float)F_dashCooldown.GetValue(player),
                DashActive = (bool)F_dashActive.GetValue(player),
                IsDashingRight = (bool)F_isDashingRight.GetValue(player),
                DashBuffer = (bool)F_dashBuffer.GetValue(player),
                TimeSinceLastDash = (float)F_timeSinceLastDash.GetValue(player),

                JumpBuffer = (bool)F_jumpBuffer.GetValue(player),
                MostRecentUpwardsMoveWasJump = (bool)F_MostRecentUpwardsMoveWasJump.GetValue(player),

                WallJumpDirection = player.wallJumpDirection,
                WallJumpMovementLockRemaining = player.wallJumpMovementLockRemaining,
                IsFinalWallClingActive = (bool)F_isFinalWallClingActive.GetValue(player),
                FinalWallClingTimeLeft = (float)F_finalWallClingTimeLeft.GetValue(player),

                TimeInAir = (float)F_timeInAir.GetValue(player),
                HeightOfLastJumpStart = (float)F_heightOfLastJumpStart.GetValue(player),

                XMoveAxis = (float)F_xMoveAxis.GetValue(player),
                IsDead = (bool)F_isDead.GetValue(player),

                FacingRight = player.facingRight,
                CutsceneMode = player.cutsceneMode,

                DashUnlocked = player.dashUnlocked,
                WallJumpUnlocked = player.wallJumpUnlocked,
                DoubleJumpUnlocked = player.doubleJumpUnlocked,
                BlockSwapUnlocked = player.blockSwapUnlocked,
                MaxAirDashes = player.maxAirDashes,
                MaxAirJumps = player.maxAirJumps,
                MaxWallJumps = player.maxWallJumps,

                RespawnPoint = player.respawnPoint,
            };
        }

        public static void Restore(Movement player, Snapshot snap)
        {
            var body = (Rigidbody2D)F_body.GetValue(player);

            // Leave body.simulated alone — toggling it destroys the contact cache,
            // and between synchronous field writes no physics step happens anyway.

            // Temporarily disable interpolation — when enabled, Unity smoothly
            // tweens transform.position between body.previousPosition and
            // body.position, causing transform to NOT match what we set.
            var wasInterp = body.interpolation;
            body.interpolation = RigidbodyInterpolation2D.None;

            // Set transform first (SyncTransforms will sync transform→body after,
            // so we set body last to win). They can differ after FixedUpdate.
            var pos = new Vector3(snap.Position.x, snap.Position.y, player.transform.position.z);
            player.transform.position = pos;
            body.rotation = snap.BodyRotation;
            body.linearVelocity = snap.Velocity;
            body.angularVelocity = snap.AngularVelocity;
            F_momentum.SetValue(player, snap.Momentum);
            F_lastAppliedMomentum.SetValue(player, snap.LastAppliedMomentum);

            F_onGround.SetValue(player, snap.OnGround);
            F_OnWall.SetValue(player, snap.OnWall);
            F_coyoteFrames.SetValue(player, snap.CoyoteFrames);
            F_wallCoyoteFrames.SetValue(player, snap.WallCoyoteFrames);
            F_wallCoyoteDir.SetValue(player, snap.WallCoyoteDir);

            F_airDashesLeft.SetValue(player, snap.AirDashesLeft);
            F_airJumpsLeft.SetValue(player, snap.AirJumpsLeft);
            F_wallJumpsLeft.SetValue(player, snap.WallJumpsLeft);

            F_dashCooldown.SetValue(player, snap.DashCooldown);
            F_dashActive.SetValue(player, snap.DashActive);
            F_isDashingRight.SetValue(player, snap.IsDashingRight);
            F_dashBuffer.SetValue(player, snap.DashBuffer);
            F_timeSinceLastDash.SetValue(player, snap.TimeSinceLastDash);

            F_jumpBuffer.SetValue(player, snap.JumpBuffer);
            F_MostRecentUpwardsMoveWasJump.SetValue(player, snap.MostRecentUpwardsMoveWasJump);

            player.wallJumpDirection = snap.WallJumpDirection;
            player.wallJumpMovementLockRemaining = snap.WallJumpMovementLockRemaining;
            F_isFinalWallClingActive.SetValue(player, snap.IsFinalWallClingActive);
            F_finalWallClingTimeLeft.SetValue(player, snap.FinalWallClingTimeLeft);

            F_timeInAir.SetValue(player, snap.TimeInAir);
            F_heightOfLastJumpStart.SetValue(player, snap.HeightOfLastJumpStart);

            F_xMoveAxis.SetValue(player, snap.XMoveAxis);
            F_isDead.SetValue(player, snap.IsDead);

            player.facingRight = snap.FacingRight;
            player.cutsceneMode = snap.CutsceneMode;

            player.dashUnlocked = snap.DashUnlocked;
            player.wallJumpUnlocked = snap.WallJumpUnlocked;
            player.doubleJumpUnlocked = snap.DoubleJumpUnlocked;
            player.blockSwapUnlocked = snap.BlockSwapUnlocked;
            player.maxAirDashes = snap.MaxAirDashes;
            player.maxAirJumps = snap.MaxAirJumps;
            player.maxWallJumps = snap.MaxWallJumps;

            player.respawnPoint = snap.RespawnPoint;

            // Set facing scale to match
            player.transform.localScale = new Vector3(snap.FacingRight ? 1f : -1f, 1f, 1f);

            body.WakeUp();
            Plugin.DbgLog($"  RESTORE-INPUT: snap.Position=({snap.Position.x:F4},{snap.Position.y:F4}) snap.BodyPosition=({snap.BodyPosition.x:F4},{snap.BodyPosition.y:F4}) snap.Velocity=({snap.Velocity.x:F4},{snap.Velocity.y:F4})");
            Plugin.DbgLog($"  RESTORE-STEP-A (after field assigns): body=({body.position.x:F4},{body.position.y:F4}) tr=({player.transform.position.x:F4},{player.transform.position.y:F4})");
            Physics2D.SyncTransforms();
            Plugin.DbgLog($"  RESTORE-STEP-B (after SyncTransforms): body=({body.position.x:F4},{body.position.y:F4}) tr=({player.transform.position.x:F4},{player.transform.position.y:F4})");
            body.position = snap.BodyPosition;
            Plugin.DbgLog($"  RESTORE-STEP-C (after body.position=): body=({body.position.x:F4},{body.position.y:F4}) tr=({player.transform.position.x:F4},{player.transform.position.y:F4})");

            // BROADPHASE NUDGE — force Box2D to re-discover contacts at the new
            // position. Setting position alone doesn't invalidate the broadphase;
            // a nudge + sync forces re-evaluation. Then a zero-dt Simulate runs
            // contact discovery (without integrating velocity).
            int nBefore = 0;
            var ctsB = new ContactPoint2D[8];
            try { nBefore = body.GetContacts(ctsB); } catch {}

            body.WakeUp();
            // Nudge position by tiny offset, sync, then put it back, sync again.
            // This forces Box2D's broadphase to re-evaluate.
            var targetPos = snap.BodyPosition;
            body.position = targetPos + new Vector2(0.01f, 0.01f);
            Physics2D.SyncTransforms();
            body.position = targetPos;
            Physics2D.SyncTransforms();
            // Now run zero-dt Simulate to trigger contact discovery
            var prevModeD = Physics2D.simulationMode;
            if (prevModeD != SimulationMode2D.Script)
                Physics2D.simulationMode = SimulationMode2D.Script;
            try { Physics2D.Simulate(0f); } finally
            {
                if (prevModeD != SimulationMode2D.Script)
                    Physics2D.simulationMode = prevModeD;
            }
            // Re-set position/velocity in case anything moved them
            body.position = snap.BodyPosition;
            body.linearVelocity = snap.Velocity;

            int nAfterNudge = 0;
            try { nAfterNudge = body.GetContacts(ctsB); } catch {}
            Plugin.DbgLog($"  RESTORE-STEP-D: BroadphaseNudge n_contacts before={nBefore} after={nAfterNudge}");
            // Re-set position/velocity in case Simulate nudged them
            body.position = snap.BodyPosition;
            body.linearVelocity = snap.Velocity;
            body.interpolation = wasInterp;
            Plugin.DbgLog($"  RESTORE-STEP-E (final): body=({body.position.x:F4},{body.position.y:F4}) tr=({player.transform.position.x:F4},{player.transform.position.y:F4})");

            // Readback verification: did the values actually take?
            var readback = Capture(player);
            bool posOk = Vector2.Distance(readback.Position, snap.Position) < 0.01f;
            bool bodyPosOk = Vector2.Distance(readback.BodyPosition, snap.BodyPosition) < 0.01f;
            bool velOk = Vector2.Distance(readback.Velocity, snap.Velocity) < 0.01f;
            bool momOk = Vector2.Distance(readback.Momentum, snap.Momentum) < 0.01f;
            bool facOk = readback.FacingRight == snap.FacingRight;
            bool wjdOk = Mathf.Abs(readback.WallJumpDirection - snap.WallJumpDirection) < 0.01f;
            bool wjlOk = Mathf.Abs(readback.WallJumpMovementLockRemaining - snap.WallJumpMovementLockRemaining) < 0.01f;
            if (!posOk || !bodyPosOk || !velOk || !momOk || !facOk || !wjdOk || !wjlOk)
            {
                Plugin.Log.LogError($"RESTORE READBACK MISMATCH:");
                if (!posOk) Plugin.Log.LogError($"  transform.pos: wrote {snap.Position}, read {readback.Position}");
                if (!bodyPosOk) Plugin.Log.LogError($"  body.pos: wrote {snap.BodyPosition}, read {readback.BodyPosition}");
                if (!velOk) Plugin.Log.LogError($"  vel: wrote {snap.Velocity}, read {readback.Velocity}");
                if (!momOk) Plugin.Log.LogError($"  mom: wrote {snap.Momentum}, read {readback.Momentum}");
                if (!facOk) Plugin.Log.LogError($"  facingRight: wrote {snap.FacingRight}, read {readback.FacingRight}");
                if (!wjdOk) Plugin.Log.LogError($"  wallJumpDir: wrote {snap.WallJumpDirection}, read {readback.WallJumpDirection}");
                if (!wjlOk) Plugin.Log.LogError($"  wallJumpLock: wrote {snap.WallJumpMovementLockRemaining}, read {readback.WallJumpMovementLockRemaining}");
            }
            Plugin.DbgLog($"Restore readback: transformPos={posOk} bodyPos={bodyPosOk} vel={velOk} mom={momOk} fac={facOk} wjd={wjdOk} wjl={wjlOk} bodyType={body.bodyType} gravScale={body.gravityScale} simulated={body.simulated} interp={body.interpolation}");
            Plugin.DbgLog($"  POST-RESTORE-IMMEDIATE: body.pos=({body.position.x:F4},{body.position.y:F4}) transform.pos=({player.transform.position.x:F4},{player.transform.position.y:F4})");
        }

        // === Unity 6 LowLevelPhysics2D PhysicsBody.SetTransformTarget bridge ===
        // The new low-level API can teleport a body while properly establishing
        // contacts (designed for this exact case). We access it via reflection
        // since there's no public accessor from Rigidbody2D → PhysicsBody.

        private static System.Type _physicsBodyType;
        private static System.Type _physicsTransformType;
        private static System.Type _physicsRotateType;
        private static System.Type _physicsWorldType;
        private static MethodInfo _physicsWorldGetDefault;
        private static MethodInfo _physicsWorldGetBodies;
        private static PropertyInfo _physicsBodyPosition;
        private static MethodInfo _physicsBodySetTransformTarget;
        private static MethodInfo _physicsRotateFromAngle;
        private static ConstructorInfo _physicsTransformCtor;
        private static bool _reflectionInitTried;

        private static bool InitReflection()
        {
            if (_reflectionInitTried) return _physicsBodyType != null;
            _reflectionInitTried = true;
            try
            {
                var asm = typeof(Rigidbody2D).Assembly;
                _physicsBodyType = asm.GetType("UnityEngine.LowLevelPhysics2D.PhysicsBody");
                _physicsTransformType = asm.GetType("UnityEngine.LowLevelPhysics2D.PhysicsTransform");
                _physicsRotateType = asm.GetType("UnityEngine.LowLevelPhysics2D.PhysicsRotate");
                _physicsWorldType = asm.GetType("UnityEngine.LowLevelPhysics2D.PhysicsWorld");

                if (_physicsBodyType == null || _physicsWorldType == null)
                {
                    Plugin.Log.LogWarning($"InitReflection: PhysicsBody or PhysicsWorld type not found (Unity version too old?)");
                    return false;
                }

                _physicsWorldGetDefault = _physicsWorldType.GetProperty("defaultPhysicsWorld",
                    BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                if (_physicsWorldGetDefault == null)
                {
                    // Try alternate names
                    var prop = _physicsWorldType.GetProperty("defaultWorld",
                        BindingFlags.Public | BindingFlags.Static);
                    _physicsWorldGetDefault = prop?.GetGetMethod();
                }

                _physicsWorldGetBodies = _physicsWorldType.GetMethod("GetBodies",
                    BindingFlags.Public | BindingFlags.Instance);
                _physicsBodyPosition = _physicsBodyType.GetProperty("position",
                    BindingFlags.Public | BindingFlags.Instance);
                _physicsBodySetTransformTarget = _physicsBodyType.GetMethod("SetTransformTarget",
                    BindingFlags.Public | BindingFlags.Instance);
                if (_physicsBodySetTransformTarget == null)
                    _physicsBodySetTransformTarget = _physicsBodyType.GetMethod("SetTransformTarget",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                if (_physicsRotateType != null)
                    _physicsRotateFromAngle = _physicsRotateType.GetMethod("FromAngle",
                        BindingFlags.Public | BindingFlags.Static);

                if (_physicsTransformType != null)
                    _physicsTransformCtor = _physicsTransformType.GetConstructor(
                        new[] { typeof(Vector2), _physicsRotateType });

                Plugin.DbgLog($"InitReflection: PhysicsBody={_physicsBodyType != null} GetBodies={_physicsWorldGetBodies != null} SetTransformTarget={_physicsBodySetTransformTarget != null} TransformCtor={_physicsTransformCtor != null}");
                return _physicsBodySetTransformTarget != null;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"InitReflection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Find the PhysicsBody matching the given Rigidbody2D (by position match)
        /// and call SetTransformTarget to teleport with proper contact establishment.
        /// Returns the contact count after the operation, or -1 on failure.
        /// </summary>
        private static int TrySetTransformTargetForRigidbody(Rigidbody2D body, Vector2 targetPos, float targetRotDegrees)
        {
            if (!InitReflection()) return -1;
            try
            {
                // Get default physics world
                var world = _physicsWorldGetDefault?.Invoke(null, null);
                if (world == null) { Plugin.DbgLog("TrySetTransformTarget: no default world"); return -1; }
                Plugin.DbgLog($"TrySetTransformTarget: world={world} type={world.GetType().FullName}");

                // Get all bodies — returns NativeArray<PhysicsBody>
                var bodies = _physicsWorldGetBodies.Invoke(world, new object[] { 0 /*Allocator.Temp*/ });
                if (bodies == null) { Plugin.DbgLog("TrySetTransformTarget: GetBodies returned null"); return -1; }

                // NativeArray<PhysicsBody> — get Length and indexer via reflection
                var bodiesType = bodies.GetType();
                Plugin.DbgLog($"TrySetTransformTarget: bodies type={bodiesType.FullName}");
                var lengthProp = bodiesType.GetProperty("Length");
                int length = (int)lengthProp.GetValue(bodies);
                Plugin.DbgLog($"TrySetTransformTarget: bodies count={length}");
                var indexer = bodiesType.GetMethod("get_Item");

                // Find body with matching position (within tolerance)
                Vector2 currentPos = body.position;
                object matchedBody = null;
                float bestDist = float.MaxValue;
                for (int i = 0; i < length; i++)
                {
                    var pb = indexer.Invoke(bodies, new object[] { i });
                    var pbPos = (Vector2)_physicsBodyPosition.GetValue(pb);
                    float d = Vector2.Distance(pbPos, currentPos);
                    if (i < 3) Plugin.DbgLog($"TrySetTransformTarget: body[{i}] pos=({pbPos.x:F2},{pbPos.y:F2}) dist={d:F2}");
                    if (d < bestDist) { bestDist = d; matchedBody = pb; }
                }

                // Dispose the NativeArray
                var disposeMethod = bodiesType.GetMethod("Dispose", System.Type.EmptyTypes);
                disposeMethod?.Invoke(bodies, null);

                if (matchedBody == null || bestDist > 0.5f)
                {
                    Plugin.DbgLog($"TrySetTransformTarget: no match (best dist={bestDist})");
                    return -1;
                }

                // Build PhysicsTransform(targetPos, PhysicsRotate.FromAngle(rotDeg))
                var rot = _physicsRotateFromAngle.Invoke(null, new object[] { targetRotDegrees });
                var pt = _physicsTransformCtor.Invoke(new object[] { targetPos, rot });

                // Call SetTransformTarget(transform, fixedDeltaTime)
                _physicsBodySetTransformTarget.Invoke(matchedBody, new object[] { pt, Time.fixedDeltaTime });

                // Read contacts after
                var ctsAfter = new ContactPoint2D[8];
                int n = body.GetContacts(ctsAfter);
                return n;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"TrySetTransformTarget exception: {ex.Message}");
                return -1;
            }
        }
    }
}

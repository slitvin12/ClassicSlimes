using System;
using System.Collections;
using System.Reflection;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using UnityEngine;

namespace ClassicSlimes
{
    /// <summary>
    /// Reflection-only integration with Lycoreia 2.x (and legacy SR2MP): tracks live co-op role,
    /// actor simulation ownership, spawn authority, spawn announcements and remote player avatars
    /// with zero compile-time dependency. Solo play works identically without any co-op mod.
    /// </summary>
    internal static class CoopCompat
    {
        static float _nextProbe, _nextPoll;
        static bool _bound, _hasLycoreia, _hasSr2mp;
        static bool _isCoop, _isHost, _isGuest;

        static Func<bool> _fnHost, _fnClient;
        static Func<Vector3, bool> _fnSpawnLeader;
        static Action<GameObject> _fnAnnounce;
        static Func<GameObject, object> _fnActorLinkOf;
        static PropertyInfo _propLocallyOwned, _propLocalVacPred;
        static FieldInfo _fldAvatarsAll, _fldAvatarRoot, _fldAvatarTransform;
        static FieldInfo _fldVacExpelDepth, _fldHostConsumeDepth;

        public static bool IsCoop { get { Poll(); return _isCoop; } }
        public static bool IsHost { get { Poll(); return _isHost; } }
        public static bool IsGuest { get { Poll(); return _isGuest; } }

        static void Poll()
        {
            float now = Time.unscaledTime;
            if (!_bound && now >= _nextProbe)
            {
                _nextProbe = now + 5f;
                try { Probe(); } catch { }
            }
            if (!_bound || now < _nextPoll) return;
            _nextPoll = now + 0.25f;
            try
            {
                bool h = _fnHost != null && _fnHost();
                bool c = _fnClient != null && _fnClient();
                _isHost = h;
                _isGuest = !h && c;
                _isCoop = h || c;
            }
            catch
            {
                _isHost = _isGuest = _isCoop = false;
            }
        }

        static void Probe()
        {
            Assembly lyc = null, sr2mp = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = a.FullName;
                if (n == null) continue;
                if (n.StartsWith("Lycoreia,", StringComparison.Ordinal)) lyc = a;
                else if (n.StartsWith("SR2MP,", StringComparison.Ordinal)) sr2mp = a;
            }
            if (lyc != null)
            {
                _hasLycoreia = true;
                const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _fnHost ??= StaticBoolFlag(lyc.GetType("Lycoreia.Compat.ModFacade+Server"), "IsRunning")
                    ?? StaticBoolFlag(lyc.GetType("Lycoreia.Mod"), "IsHost");
                _fnClient ??= StaticBoolFlag(lyc.GetType("Lycoreia.Compat.ModFacade+Client"), "IsConnected")
                    ?? StaticBoolFlag(lyc.GetType("Lycoreia.Mod"), "IsNetworkActive");

                if (_fnSpawnLeader == null)
                {
                    var m = lyc.GetType("Lycoreia.Game.ActorRuntime")?.GetMethod("IsSpawnLeader", S, null, new[] { typeof(Vector3) }, null);
                    if (m != null && m.ReturnType == typeof(bool))
                        _fnSpawnLeader = (Func<Vector3, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, bool>), m);
                }

                if (_fnAnnounce == null)
                {
                    var m = lyc.GetType("Lycoreia.Game.Hooks.InstantiateActorHook")?.GetMethod("Postfix", S, null, new[] { typeof(GameObject) }, null);
                    if (m != null && m.ReturnType == typeof(void))
                        _fnAnnounce = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), m);
                }

                if (_fnActorLinkOf == null)
                {
                    var linkType = lyc.GetType("Lycoreia.Game.ActorLink");
                    var ofMethod = linkType?.GetMethod("Of", S, null, new[] { typeof(GameObject) }, null);
                    var ownedProp = linkType?.GetProperty("LocallyOwned", I);
                    var predProp = linkType?.GetProperty("LocalVacuumPrediction", I);
                    if (ofMethod != null && ownedProp != null && predProp != null)
                    {
                        _propLocallyOwned = ownedProp;
                        _propLocalVacPred = predProp;
                        _fnActorLinkOf = go => ofMethod.Invoke(null, new object[] { go });
                    }
                }

                if (_fldAvatarsAll == null)
                {
                    var avatarsType = lyc.GetType("Lycoreia.Game.Avatars.RemoteAvatars");
                    var avatarType = lyc.GetType("Lycoreia.Game.Avatars.RemoteAvatar");
                    var allFld = avatarsType?.GetField("All", S);
                    var rootFld = avatarType?.GetField("Root", I);
                    var trFld = avatarType?.GetField("transform", I);
                    if (allFld != null && rootFld != null)
                    {
                        _fldAvatarsAll = allFld;
                        _fldAvatarRoot = rootFld;
                        _fldAvatarTransform = trFld;
                    }
                }

                _fldVacExpelDepth ??= lyc.GetType("Lycoreia.Game.Hooks.VacuumExpelHook")?.GetField("Depth", S);
                _fldHostConsumeDepth ??= lyc.GetType("Lycoreia.Game.Hooks.SlimeFoodAuthorityHook")?.GetField("HostConsumeDepth", S);

                _bound = _fnHost != null && _fnClient != null;
                if (_bound) ClassicSlimesMod.Log?.Msg("Co-op compatibility active: Lycoreia detected");
                return;
            }

            if (sr2mp != null)
            {
                _hasSr2mp = true;
                const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var main = sr2mp.GetType("SR2MP.Main");
                var globals = sr2mp.GetType("SR2MP.GlobalVariables");
                _fnHost ??= InstanceBoolFlag(main, "Server", "IsRunning");
                _fnClient ??= InstanceBoolFlag(main, "Client", "IsConnected");
                if (_fnAnnounce == null)
                {
                    try
                    {
                        var mgr = globals?.GetField("ActorManager", S)?.GetValue(null);
                        var reg = mgr?.GetType().GetMethod("RegisterSpawnOverNetwork", I, null, new[] { typeof(GameObject) }, null);
                        if (reg != null && reg.ReturnType == typeof(void))
                            _fnAnnounce = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), mgr, reg);
                    }
                    catch { }
                }
                _bound = _fnHost != null && _fnClient != null;
                if (_bound) ClassicSlimesMod.Log?.Msg("Co-op compatibility active: SR2MP detected");
            }
        }

        static Func<bool> StaticBoolFlag(Type holder, string name)
        {
            if (holder == null) return null;
            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = holder.GetProperty(name, S)?.GetGetMethod(true);
            if (prop != null && prop.ReturnType == typeof(bool))
                return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), prop);
            var fld = holder.GetField(name, S);
            if (fld != null && fld.FieldType == typeof(bool))
                return () => (bool)fld.GetValue(null);
            return null;
        }

        static Func<bool> InstanceBoolFlag(Type holder, string instanceProp, string flag)
        {
            if (holder == null) return null;
            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = holder.GetProperty(instanceProp, S);
            if (p == null) return null;
            var flagProp = p.PropertyType.GetProperty(flag, I)?.GetGetMethod(true);
            if (flagProp == null || flagProp.ReturnType != typeof(bool)) return null;
            return () =>
            {
                var inst = p.GetValue(null);
                return inst != null && (bool)flagProp.Invoke(inst, null);
            };
        }

        /// <summary>True when offline, or when this peer simulates the rigidbody (Host/owner, not a kinematic remote replica).</summary>
        public static bool IsLocallySimulated(Rigidbody rb)
        {
            Poll();
            if (!_isCoop) return rb == null || !rb.isKinematic;
            if (rb != null && _fnActorLinkOf != null && _propLocallyOwned != null)
            {
                try
                {
                    var link = _fnActorLinkOf(rb.gameObject);
                    if (link != null) return (bool)_propLocallyOwned.GetValue(link);
                }
                catch { }
            }
            return rb != null && !rb.isKinematic;
        }

        /// <summary>True when offline, or when this peer is the spawn leader for the given world position.</summary>
        public static bool IsSpawnAuthority(Vector3 pos)
        {
            Poll();
            if (!_isCoop) return true;
            if (_fnSpawnLeader != null)
            {
                try { return _fnSpawnLeader(pos); } catch { }
            }
            return _isHost;
        }

        /// <summary>Publishes an actor instantiated via GameModel/InstantiateActorFromModel to the co-op session.</summary>
        public static void AnnounceSpawn(GameObject go)
        {
            Poll();
            if (!_isCoop || go == null) return;
            if (_fnAnnounce == null && _hasSr2mp)
            {
                try
                {
                    foreach (var m in MelonLoader.MelonMod.RegisteredMelons)
                    {
                        var asm = m.MelonAssembly?.Assembly;
                        if (asm == null || !string.Equals(asm.GetName().Name, "SR2MP", StringComparison.OrdinalIgnoreCase)) continue;
                        var globals = asm.GetType("SR2MP.GlobalVariables");
                        var mgr = globals?.GetField("ActorManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                        var reg = mgr?.GetType().GetMethod("RegisterSpawnOverNetwork", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(GameObject) }, null);
                        if (reg != null && reg.ReturnType == typeof(void))
                            _fnAnnounce = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), mgr, reg);
                        break;
                    }
                }
                catch { }
            }
            if (_fnAnnounce == null) return;
            bool incDepth = _isGuest && _fldVacExpelDepth != null;
            int prevDepth = 0;
            if (incDepth)
            {
                try { prevDepth = (int)(_fldVacExpelDepth.GetValue(null) ?? 0); _fldVacExpelDepth.SetValue(null, prevDepth + 1); }
                catch { incDepth = false; }
            }
            try { _fnAnnounce(go); }
            catch { }
            finally
            {
                if (incDepth) try { _fldVacExpelDepth.SetValue(null, prevDepth); } catch { }
            }
        }

        /// <summary>Destroys a scene/shared actor on the Host with Lycoreia HostConsumeDepth when suppressing guest authority doesn't apply.</summary>
        public static void DestroySharedActor(GameObject go, string source, bool okForNonNetwork = false)
        {
            if (go == null) return;
            Poll();
            bool incHost = _isCoop && _isHost && _fldHostConsumeDepth != null;
            int prevDepth = 0;
            if (incHost)
            {
                try { prevDepth = (int)(_fldHostConsumeDepth.GetValue(null) ?? 0); _fldHostConsumeDepth.SetValue(null, prevDepth + 1); }
                catch { incHost = false; }
            }
            try { Destroyer.DestroyActor(go, source, okForNonNetwork); }
            finally
            {
                if (incHost) try { _fldHostConsumeDepth.SetValue(null, prevDepth); } catch { }
            }
        }

        /// <summary>True when held by the local player's vacpack (prevents remote ReplicaMotion.SetHeld from triggering Glitch debug rewards/despawn).</summary>
        public static bool IsHeldByLocalVac(Vacuumable vac)
        {
            if (vac == null || !vac.IsHeld()) return false;
            Poll();
            if (!_isCoop) return true;

            try
            {
                var vi = SceneContext.Instance?.PlayerState?.VacuumItem;
                if (vi == null) return false;
                if (vi._held != null) return vi._held.Pointer == vac.gameObject.Pointer;
                if (vac.Size == VacuumableSize.LARGE) return false;
                if (_fnActorLinkOf != null && _propLocalVacPred != null)
                {
                    var link = _fnActorLinkOf(vac.gameObject);
                    if (link != null) return (bool)_propLocalVacPred.GetValue(link);
                }
                return vi._vacPressed && vi._vacMode == VacuumItem.VacMode.VAC && U.DistToPlayer(vac.transform.position) < Mathf.Max(8f, vi.MaxVacDist + 2f);
            }
            catch { return false; }
        }

        /// <summary>Nearest active player (local player or remote co-op avatar) to <paramref name="pos"/>.</summary>
        public static GameObject NearestPlayer(Vector3 pos, out Vector3 playerPos, out float dist)
        {
            var local = U.Player;
            GameObject best = local;
            if (local != null)
            {
                playerPos = local.transform.position;
                dist = Vector3.Distance(pos, playerPos);
            }
            else
            {
                playerPos = pos;
                dist = float.MaxValue;
            }

            Poll();
            if (!_isCoop || _fldAvatarsAll == null) return best;

            try
            {
                if (_fldAvatarsAll.GetValue(null) is IDictionary dict && dict.Count > 0)
                {
                    foreach (var av in dict.Values)
                    {
                        if (av == null) continue;
                        var root = _fldAvatarRoot.GetValue(av) as GameObject;
                        if (root == null || !root.activeInHierarchy) continue;
                        var tr = (_fldAvatarTransform?.GetValue(av) as Transform) ?? root.transform;
                        if (tr == null) continue;
                        var rp = tr.position;
                        float d = Vector3.Distance(pos, rp);
                        if (d < dist)
                        {
                            dist = d;
                            playerPos = rp;
                            best = root;
                        }
                    }
                }
            }
            catch { }

            return best;
        }

        public static float DistToNearestPlayer(Vector3 pos)
        {
            NearestPlayer(pos, out _, out float dist);
            return dist;
        }
    }
}

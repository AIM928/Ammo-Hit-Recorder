using System;
using System.IO;
using System.Reflection;
using System.Text;
using GHPC;
using GHPC.Equipment;
using GHPC.Weapons;
using NativeShotInfo=GHPC.Weapons.ShotInfo;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly:MelonInfo(typeof(AIMAmmoHitRecorder.Mod),"Player Ammo Hit Recorder","0.1.11","AIM")]
[assembly:MelonGame("Radian Simulations LLC","GHPC")]
namespace AIMAmmoHitRecorder
{
    public sealed class Mod:MelonMod
    {
        private const string GameModule="dd894e5d-3bb0-4540-8c1a-196e2351c688";
        private static Mod owner;
        private readonly ShotAggregation buffer=new ShotAggregation();
        private StreamWriter writer;
        private bool disabled;
        private float nextFlush;
        private int lastDropped;
        [ThreadStatic] private static Capture contact;
        private sealed class Capture
        {
            internal HitRecord Row;
            internal ShotAggregation.Group Group;
            internal Capture Previous;
            internal LiveRound Round;
            internal bool Committed;
        }
        public override void OnInitializeMelon()
        {
            try
            {
                if(typeof(LiveRound).Assembly.ManifestModule.ModuleVersionId!=new Guid(GameModule))
                    throw new NotSupportedException("Game build differs; recorder skipped until its read-only contract is checked.");
                var target=typeof(LiveRound).GetMethod("penCheck",BindingFlags.NonPublic|BindingFlags.Instance,null,
                    new[]{typeof(Collider),typeof(Vector3),typeof(Vector3),typeof(Vector3),typeof(GHPC.AI.Interfaces.ITarget)},null);
                if(target==null||target.ReturnType!=typeof(bool))throw new MissingMethodException("LiveRound.penCheck");
                LanguageSettings.Initialize();
                string directory=Path.Combine(MelonEnvironment.UserDataDirectory,"AIMAmmoHits");
                Directory.CreateDirectory(directory);
                string file=Path.Combine(directory,"player-hits-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"Z-"+Guid.NewGuid().ToString("N").Substring(0,6)+".csv");
                writer=new StreamWriter(new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(true));
                writer.WriteLine(RecordRules.Header);writer.Flush();
                owner=this;
                HarmonyInstance.Patch(target,
                    prefix:new HarmonyMethod(typeof(Mod),nameof(BeforeContact)){priority=HarmonyLib.Priority.First},
                    postfix:new HarmonyMethod(typeof(Mod),nameof(AfterContact)){priority=HarmonyLib.Priority.Last},
                    transpiler:new HarmonyMethod(typeof(NativeObservation),nameof(NativeObservation.Transpile)){priority=HarmonyLib.Priority.Last},
                    finalizer:new HarmonyMethod(typeof(Mod),nameof(FinalContact)),ilmanipulator:null);
                HarmonyInstance.Patch(typeof(LiveRound).GetMethod("Init"),postfix:new HarmonyMethod(typeof(Mod),nameof(AfterInit)));
                HarmonyInstance.Patch(typeof(NativeShotInfo).GetMethod("NotifyShotTerminated"),postfix:new HarmonyMethod(typeof(Mod),nameof(BeforeTerminated)));
                MelonLogger.Msg(LanguageSettings.Choose("[AmmoHit] 警告：卡死/疑似自伤问题尚未修复，本版仅改显示。CSV=","[AmmoHit] WARNING: reported hang/self-damage issues remain unresolved; this update changes display only. CSV=")+file);
                MelonLogger.Msg(LanguageSettings.Choose("[AmmoHit] 显示语言：中文；在[AIMAmmoHitRecorder]中设置Language=auto/zh/en，修改后重启。","[AmmoHit] Display language: English; set Language=auto/zh/en under [AIMAmmoHitRecorder], then restart."));
            }
            catch(Exception e){Fault(e);}
        }
        private static ShotAggregation.Group Register(LiveRound round)
        {
            Vector3 launch;NativeShotInfo root;
            if(round==null||round.Info==null||round.IsSpall||round.Pooled||!PlayerOrigin(round.ShotInfo,out launch,out root))return null;
            return owner.buffer.Register(root,round.ShotInfo);
        }
        private static void AfterInit(LiveRound __instance)
        {
            if(owner==null||owner.disabled)return;
            try{Register(__instance);}catch(Exception e){owner.Fault(e);}
        }
        private static void BeforeTerminated(NativeShotInfo __instance)
        {
            if(owner==null||owner.disabled)return;
            try{owner.buffer.Terminate(__instance,Time.time);}catch(Exception e){owner.Fault(e);}
        }
        private static bool PlayerOrigin(NativeShotInfo shot,out Vector3 launch,out NativeShotInfo root)
        {
            launch=default(Vector3);root=null;
            if(shot==null)return false;
            for(int depth=0;depth<16;depth++)
            {
                if(shot.ParentShot==null){root=shot;launch=shot.StartPosition;return shot.IsPlayerShot;}
                shot=shot.ParentShot;
            }
            return false;
        }
        private static void BeforeContact(LiveRound __instance,Collider __0,Vector3 __1,Vector3 __2,Vector3 __3,out Capture __state)
        {
            __state=null;var current=owner;
            if(current==null||current.disabled)return;
            // An ignored nested contact still needs a barrier so its observers
            // cannot accidentally attach to the outer contact of the same round.
            __state=new Capture{Previous=contact};contact=__state;
            try
            {
                Vector3 launch;NativeShotInfo root;
                if(__instance==null||__0==null||__instance.Info==null||__instance.IsSpall||__instance.Pooled||!PlayerOrigin(__instance.ShotInfo,out launch,out root))return;
                // Match only the native notify-and-pass Detection branch, not all triggers.
                if(__0.CompareTag("Detection")&&__0.GetComponent<ShotDetectionZone>()!=null)return;
                IArmor armor=__0.GetComponent<IArmor>();
                if(armor!=null&&ReferenceEquals(armor.Unit,__instance.Shooter))return;
                // Unknown collider normals are not armor measurements. In particular,
                // TRACKING OBJECT must never consume the real hit of this shot.
                if(armor==null&&!(__0 is TerrainCollider))return;
                var group=Register(__instance);if(group==null||!current.buffer.BeginHit(group))return;
                var row=new HitRecord{Time=Time.time,Round=__instance.ID,Collider=__0.GetInstanceID(),Frame=Time.frameCount,
                    Ammo=__instance.Info.Name,Surface=armor==null?__0.name:armor.Name,
                    Kind=armor!=null?"armor":"terrain",
                    SpeedBefore=__instance.CurrentSpeed,PenBefore=__instance.CurrentPenRating,BasePen=__instance.Info.RhaPenetration,
                    Obliquity=RecordRules.Angle(__2.x,__2.y,__2.z,__1.x,__1.y,__1.z),
                    X=__3.x,Y=__3.y,Z=__3.z,Child=__instance.ParentRound!=null,
                    StartX=launch.x,StartY=launch.y,StartZ=launch.z,
                    Range=RecordRules.Range(launch.x,launch.y,launch.z,__3.x,__3.y,__3.z)};
                __state.Row=row;__state.Group=group;__state.Round=__instance;
                // Reserve first-contact kinematics before any native child call.
                if(group.First==null)group.First=row;
            }
            catch(Exception e){current.Fault(e);}
        }
        private static void AfterContact(LiveRound __instance,bool __result,Capture __state)
        {
            var current=owner;if(current==null||current.disabled||__state==null)return;
            try
            {
                if(__state.Row==null)return;
                var row=__state.Row;row.Continue=__result;
                if(__instance!=null&&__instance.ID==row.Round&&!__instance.Pooled&&__instance.Info!=null)
                {row.SpeedAfter=__instance.CurrentSpeed;row.PenAfter=__instance.CurrentPenRating;}
                current.buffer.Add(__state.Group,row);
                __state.Committed=true;
                // No file I/O, logging, extra armor calculations or world scans in this hook.
            }
            catch(Exception e){current.Fault(e);}
            finally{if(ReferenceEquals(contact,__state))contact=__state.Previous;}
        }
        // Finalizers return void: native exceptions are never replaced or suppressed.
        private static void FinalContact(Capture __state,Exception __exception)
        {
            if(__state==null)return;
            if(ReferenceEquals(contact,__state))contact=__state.Previous;
            if(__exception!=null&&owner!=null)
            {
                owner.buffer.MarkIncomplete(__state.Group);
                if(__state.Row!=null&&!__state.Committed)
                {
                    __state.Row.Outcome="unobserved";
                    owner.buffer.Add(__state.Group,__state.Row);__state.Committed=true;
                }
            }
        }
        internal static void ObserveDecision(LiveRound round,float resistance,float capability)
        {
            var c=contact;if(owner==null||c==null||!ReferenceEquals(c.Round,round))return;
            c.Row.DecisionResistance=resistance;c.Row.DecisionPen=capability;
        }
        internal static void ObserveOutcome(LiveRound round,int outcome)
        {
            var c=contact;if(owner==null||c==null||!ReferenceEquals(c.Round,round))return;
            c.Row.Outcome=outcome==1?"perforated":outcome==2?"not_perforated":"ricochet";
        }
        public override void OnUpdate()
        {
            if(disabled||writer==null||Time.realtimeSinceStartup<nextFlush)return;
            nextFlush=Time.realtimeSinceStartup+.25f;buffer.FinishReady(Time.time,false);Drain(128);
        }
        private void Drain(int limit)
        {
            if(disabled||writer==null)return;
            try
            {
                for(int i=0;i<limit&&buffer.Count>0;i++)
                {
                    var row=buffer.Dequeue();writer.WriteLine(RecordRules.Csv(row));
                    MelonLogger.Msg(RecordRules.Summary(row));
                }
                writer.Flush();
                if(buffer.Dropped!=lastDropped)
                {
                    if(lastDropped==0)MelonLogger.Warning(LanguageSettings.Choose("[AmmoHit] 记录达到上限，部分发射可能漏记。待处理和队列上限1024，每局最多登记100000发。","[AmmoHit] Recording limit reached; some shots may be omitted. Pending + queued: 1024; registered shots per session: 100000."));
                    lastDropped=buffer.Dropped;
                }
            }
            catch(Exception e){Fault(e);}
        }
        public override void OnSceneWasUnloaded(int buildIndex,string sceneName){buffer.FinishReady(Time.time,true);Drain(ShotAggregation.MaxQueued);}
        public override void OnDeinitializeMelon()
        {
            HarmonyInstance.UnpatchSelf();buffer.FinishReady(Time.time,true);Drain(ShotAggregation.MaxQueued);owner=null;contact=null;
            if(writer!=null){try{writer.Dispose();}catch{}writer=null;}
            buffer.Discard();
        }
        private void Fault(Exception error)
        {
            if(disabled)return;disabled=true;owner=null;HarmonyInstance.UnpatchSelf();buffer.Discard();
            if(writer!=null){try{writer.Dispose();}catch{}writer=null;}
            MelonLogger.Warning(LanguageSettings.Choose("[AmmoHit] 记录器因异常停止记录：","[AmmoHit] Recording stopped after an exception: ")+error.GetType().Name+": "+error.Message+LanguageSettings.Choose("；这不证明运行时没有副作用，请保持停用并检查日志。","; runtime safety is not established. Keep disabled and inspect the log."));
        }
    }
}

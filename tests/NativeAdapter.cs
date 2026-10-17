using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Globalization;
using GHPC;
using GHPC.Weapons;
using UnityEngine;
using HarmonyLib;

namespace AIMAmmoHitRecorder
{
    internal static class NativeAdapter
    {
        private static int checks;
        private const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
        private static object Call(string name,params object[] args){return typeof(Mod).GetMethod(name,Hidden).Invoke(null,args);}
        private static Mod Activate(out MemoryStream memory)
        {
            var mod=new Mod();memory=new MemoryStream();var writer=new StreamWriter(memory,new UTF8Encoding(false));
            writer.WriteLine(RecordRules.Header);writer.Flush();typeof(Mod).GetField("writer",Hidden).SetValue(mod,writer);
            typeof(Mod).GetField("owner",Hidden).SetValue(null,mod);return mod;
        }
        private static ShotAggregation Buffer(Mod mod){return (ShotAggregation)typeof(Mod).GetField("buffer",Hidden).GetValue(mod);}
        private static object Before(LiveRound round,Collider collider)
        {
            object[] args={round,collider,new Vector3(0,0,-1),new Vector3(0,0,1),new Vector3(3,4,0),null};
            Call("BeforeContact",args);return args[5];
        }
        private static HitRecord Row(object state){return (HitRecord)state.GetType().GetField("Row",Hidden).GetValue(state);}
        private static void EndContact(LiveRound round,object state,bool result=true)
        {Call("AfterContact",round,result,state);Call("FinalContact",state,null);}
        private static Collider Plate(string name="plate")
        {var c=new Collider();c.Components.Add(new ArmorFixture{FixtureName=name});return c;}
        private static HitRecord Hit(int id=1,string outcome="perforated",float resistance=100)
        {return new HitRecord{Round=id,Collider=id,Ammo="test",Surface="plate"+id,Kind="armor",Outcome=outcome,DecisionResistance=resistance,DecisionPen=700,PenAfter=600};}
        private static void Rules()
        {
            Check(RecordRules.Header.Split(',').Length==37,"schema count");
            for(int i=0;i<=1800;i++)
            {double a=i*Math.PI/1800;Check(Math.Abs(RecordRules.Angle(Math.Sin(a),0,Math.Cos(a),0,0,-1)-i/10d)<1e-5,"angle sweep");}
            for(int i=0;i<=900;i++)Check(Math.Abs(RecordRules.NormalPen(1600,i/10d)-1600*Math.Cos(i*Math.PI/1800))<1e-8,"reference COS changed");
            for(int i=1;i<10000;i++)Check(Math.Abs(RecordRules.Range(0,0,0,i*3,i*4,0)-i*5d)<1e-8,"range sweep");
            Check(double.IsNaN(RecordRules.NormalPen(500,120)),"backface fabricated");
            Check(double.IsNaN(RecordRules.Angle(0,0,0,0,0,1)),"invalid angle");
            Check(RecordRules.Text("=1+1")=="\"'=1+1\"","formula safety");
            Check(RecordRules.Text("a,\"b\"\nc")=="\"a,\"\"b\"\" c\"","CSV escaping");
            var r=Hit();r.ArmorContacts=1;r.Perforated=1;r.SuccessfulResistance=100;r.FirstArmor=r.LastArmor="plate";r.LastOutcome="perforated";r.Complete=true;
            Check(RecordRules.Csv(r).Split(',').Length==37,"CSV count");
            Check(RecordRules.Summary(r).Contains("已穿透：1次（部件名称未保存）")&&!RecordRules.Summary(r).Contains("COS"),"native summary not primary");
            var terrain=new HitRecord{Kind="terrain"};Check(RecordRules.Summary(terrain).Contains("不计算装甲穿透")&&!RecordRules.Summary(terrain).Contains("合计=0"),"terrain fabricated armor result");
        }
        private static string[] CsvFields(string line)
        {
            var fields=new List<string>();var value=new StringBuilder();bool quoted=false;
            for(int i=0;i<line.Length;i++)
            {
                char c=line[i];
                if(c=='"')
                {
                    if(quoted&&i+1<line.Length&&line[i+1]=='"'){value.Append('"');i++;}
                    else quoted=!quoted;
                }
                else if(c==','&&!quoted){fields.Add(value.ToString());value.Length=0;}
                else value.Append(c);
            }
            Check(!quoted,"CSV unclosed quote");fields.Add(value.ToString());return fields.ToArray();
        }
        private static void DisplayRules()
        {
            var q=new ShotAggregation();var root=new object();var g=q.Register(root,root);q.BeginHit(g);
            var first=Hit(1);first.Surface="Kontakt-5";first.Ammo="M829A2";first.Range=16.838;first.Obliquity=65.934;first.SpeedBefore=1679.994f;
            first.PenBefore=691.49f;first.DecisionPen=691.49f;first.DecisionResistance=307.73f;first.PenAfter=383.76f;
            q.Add(g,first);q.Add(g,first); // No duplicate name detail for repeated native callback.
            var passed=Hit(2);passed.Surface="special armor array";passed.DecisionPen=383.76f;passed.PenAfter=13.01f;q.Add(g,passed);
            var blocked=Hit(3,"not_perforated",86.24f);blocked.Surface="hull floor";blocked.DecisionPen=13.01f;blocked.PenAfter=13.01f;q.Add(g,blocked);
            q.Terminate(root,0);q.FinishReady(1,false);var r=q.Dequeue();var summary=RecordRules.Summary(r);
            Check(r.ArmorEvents.Count==3&&r.Perforated==2&&r.Stopped==1,"event names/counts disagree");
            Check(summary.StartsWith("[AmmoHit] #1 M829A2\n")&&summary.Contains("阶段 / 部件 | 判定 | 原生读数"),"missing staged table");
            Check(summary.Contains("未穿透：车底板×1次"),"blocked part missing");
            Check(summary.Contains("最后装甲接触：车底板（未穿透）")&&summary.Contains("已穿透：接触-5反应装甲"),"last failed contact mislabels whole shot");
            Check(summary.Contains("判定前剩余能力=13.01mmRHA")&&summary.Contains("等效阻力=86.24mmRHAe"),"last comparison values unclear");
            Check(summary.Contains("首次有效接触前 | — | 穿透能力=691.49mmRHA"),"initial penetration stage missing");
            Check(summary.Contains("1. 接触-5反应装甲 [弹丸#1] | 已穿透 | 判定前能力=691.49mmRHA；该处阻力=307.73mmRHAe；穿透后剩余能力=383.76mmRHA"),"intermediate remaining capability not snapshot");
            Check(summary.Contains("2. 特殊装甲阵列 [弹丸#2] | 已穿透")&&summary.Contains("穿透后剩余能力=13.01mmRHA"),"later stage lost");
            Check(summary.Contains("距离=16.84米")&&summary.Contains("入射角=65.93°（0°为正撞）")&&summary.Contains("速度=1679.99米/秒"),"Chinese units/rounding");
            Check(!summary.Contains("层/未穿")&&!summary.Contains("已穿等效阻力合计")&&!summary.Contains("COS"),"confusing old terminology retained");
            Check(summary.Contains("\n")&&!summary.Contains("\r"),"one-shot block not readable multiline table");
            var fields=CsvFields(RecordRules.Csv(r));Check(fields.Length==37,"detail CSV count");
            Check(fields[24]=="1"&&fields[30]=="not_perforated"&&fields[35]=="native_termination","legacy columns moved");
            Check(fields[36].Contains("1. 接触-5反应装甲（原名：Kontakt-5；弹丸#1）：已穿透")&&fields[36].Contains("3. 车底板（原名：hull floor；弹丸#3）：未穿透"),"full event details lost");
            Check(fields[36].Contains("穿透后剩余能力=383.76mmRHA")&&!fields[36].Contains("\n"),"CSV stage numbers missing or multiline row");
            Check(!summary.Contains("hull floor")&&!summary.Contains("Kontakt-5"),"known armor names not translated in console");
            Console.WriteLine("DISPLAY_EXAMPLE "+summary);

            q=new ShotAggregation();root=new object();g=q.Register(root,root);q.BeginHit(g);
            var a=Hit(10);a.Surface="hull side";q.Add(g,a);
            a=Hit(11);a.Surface="hull side";q.Add(g,a);
            a=Hit(12,"not_perforated");a.Surface="hull side";q.Add(g,a);
            a=Hit(13,"ricochet");a.Surface="turret roof";q.Add(g,a);
            a=Hit(14,"custom-unknown");a.Surface="untranslated part";q.Add(g,a);
            q.Terminate(root,0);q.FinishReady(1,false);r=q.Dequeue();summary=RecordRules.Summary(r);
            Check(summary.Contains("已穿透：车体侧板×2次")&&summary.Contains("未穿透：车体侧板×1次"),"repeated same part falsely collapsed across outcomes");
            Check(summary.Contains("跳弹：炮塔顶板×1次")&&summary.Contains("未确认：部件“untranslated part”×1次")&&!summary.Contains("原名，未翻译"),"raw name hidden or unwanted suffix retained");
            Check(summary.Contains("记录不完整，仅列出已观察到的结果"),"unknown summary presented as complete");
            Check(!RecordRules.Summary(new HitRecord()).Contains("只记录到命中地面"),"empty record invented terrain contact");
            Check(RecordRules.Summary(new HitRecord{Kind="terrain",Complete=true}).Contains("只记录到命中地面；不计算装甲穿透"),"terrain verdict");
            Check(RecordRules.Summary(new HitRecord()).Contains("距离=未记录")&&!RecordRules.Summary(new HitRecord()).Contains("距离=0"),"missing range fabricated");
            Check(RecordRules.ArmorName(null)=="部件名称未记录","missing name guessed");
            Check(RecordRules.ArmorName("lower front plate")=="首下装甲板"&&RecordRules.ArmorName("firewall")=="发动机防火隔板"&&RecordRules.ArmorName("right track bank")=="右侧履带组","screenshot part translation");
            Check(RecordRules.ArmorName("HULL SIDE")=="车体侧板","translation case sensitivity");
            Check(RecordRules.ArmorName("turret cheek backing plate")=="炮塔正面背板","backing plate confused with outer face");

            var missing=Hit(100);missing.Surface="lower front plate";missing.DecisionPen=300;missing.DecisionResistance=100;missing.PenAfter=float.NaN;
            q=new ShotAggregation();root=new object();g=q.Register(root,root);q.BeginHit(g);q.Add(g,missing);q.Terminate(root,0);q.FinishReady(1,false);r=q.Dequeue();
            summary=RecordRules.Summary(r);Check(summary.Contains("穿透后剩余能力=未记录")&&!summary.Contains("穿透后剩余能力=200"),"missing after value fabricated by subtraction");
            Check(r.ArmorEvents[0].DecisionPen==300,"snapshot initial value");missing.DecisionPen=999;Check(r.ArmorEvents[0].DecisionPen==300,"event stores mutable contact row");

            var negative=Hit(101,"not_perforated");negative.Surface="right track bank";negative.DecisionPen=-6.306f;negative.PenAfter=-6.306f;negative.Child=true;
            q=new ShotAggregation();root=new object();g=q.Register(root,root);q.BeginHit(g);q.Add(g,negative);q.Terminate(root,0);q.FinishReady(1,false);r=q.Dequeue();summary=RecordRules.Summary(r);
            Check(summary.Contains("判定前能力=-6.31mmRHA"),"negative values silently clamped");
            Check(summary.Contains("弹丸#101，子弹丸"),"child identity missing");

            q=new ShotAggregation();root=new object();g=q.Register(root,root);q.BeginHit(g);
            for(int i=0;i<300;i++)
            {
                a=Hit(i);a.Surface=i==0?"=part,\"quoted\"\nname":new string('x',160)+i;
                q.Add(g,a);
            }
            Check(g.First.ArmorEvents.Count==256&&g.Incomplete,"details exceed native event cap");
            q.FinishReady(1,true);r=q.Dequeue();fields=CsvFields(RecordRules.Csv(r));
            Check(fields.Length==37&&fields[36].Contains("256. ")&&!fields[36].Contains("257. "),"details truncated to single-label length or wrong schema");
            Check(fields[36].StartsWith("1. ")&&fields[36].Contains("=part,\"quoted\" name")&&!fields[36].Contains("\n"),"detail CSV quoting/control/formula safety");

            q=new ShotAggregation();root=new object();g=q.Register(root,root);q.BeginHit(g);
            string[] translated={"hull side","hull floor","turret","firewall","side skirt","engine","fuel tank","gun barrel"};
            for(int i=0;i<8;i++){a=Hit(i);a.Surface=translated[i];q.Add(g,a);}
            q.Terminate(root,0);q.FinishReady(1,false);r=q.Dequeue();
            Check(RecordRules.Summary(r).Contains("另4种部件，完整明细见CSV"),"console omission not disclosed");
            Check(CsvFields(RecordRules.Csv(r))[36].Contains("8. 炮管（原名：gun barrel"),"console cap lost CSV details");
            q=new ShotAggregation();root=new object();g=q.Register(root,root);q.BeginHit(g);
            for(int i=0;i<16;i++){a=Hit(i);a.Surface="hull side";q.Add(g,a);}q.Terminate(root,0);q.FinishReady(1,false);r=q.Dequeue();summary=RecordRules.Summary(r);
            Check(summary.Contains("另有4次接触未展开")&&!summary.Contains("\n13. "),"console stage cap not disclosed");
            Check(CsvFields(RecordRules.Csv(r))[36].Contains("16. "),"full CSV capped with console");
            string[] screenshotNames={"hull lower side","gunner's torso","gunner's right leg","casing bin","MG ammo bin","warhead"};
            foreach(string name in screenshotNames)Check(RecordRules.ArmorName(name)=="部件“"+name+"”","previous-version label behavior changed: "+name);
            q=new ShotAggregation();root=new object();g=q.Register(root,root);q.BeginHit(g);
            a=Hit(1);a.Surface="secret untranslated plate";q.Add(g,a);
            a=Hit(2);a.Surface="hull side";q.Add(g,a);
            a=Hit(3,"not_perforated");a.Surface="other unknown armor";q.Add(g,a);
            q.Terminate(root,0);q.FinishReady(1,false);r=q.Dequeue();summary=RecordRules.Summary(r);
            Check(summary.Contains("1. 部件“secret untranslated plate”")&&summary.Contains("2. 车体侧板")&&summary.Contains("3. 部件“other unknown armor”")&&!summary.Contains("原名，未翻译"),"unknown rows hidden or suffix retained");
            Check(!summary.Contains("未翻译部件")&&summary.Contains("最后装甲接触：部件“other unknown armor”（未穿透）"),"last raw part or verdict lost");
            Check(!summary.Contains("说明：")&&!summary.Contains("数值是游戏当时读数")&&!summary.Contains("原名，未翻译"),"unwanted explanatory footer/suffix retained");
            fields=CsvFields(RecordRules.Csv(r));Check(fields[36].Contains("secret untranslated plate")&&fields[36].Contains("other unknown armor")&&r.Perforated==2&&r.Stopped==1,"display filtering changed records or counts");
            Check(RecordRules.Summary(new HitRecord{ArmorContacts=1,Stopped=1,LastOutcome="not_perforated",LastArmor="unknown",Complete=true}).Contains("未穿透：1次（部件名称未保存）"),"legacy row lost verdict");
        }
        private static void Aggregate()
        {
            var q=new ShotAggregation();var root=new object();var child=new object();var g=q.Register(root,root);
            Check(ReferenceEquals(g,q.Register(root,root))&&g.Active==1,"double init");
            Check(ReferenceEquals(g,q.Register(root,child))&&g.Active==2,"child isolation");
            q.FinishReady(20,false);Check(q.Count==0,"miss created record");
            q.BeginHit(g);q.Add(g,Hit());q.Add(g,Hit());q.Add(g,Hit(2,"perforated",200));q.Add(g,Hit(3,"not_perforated",500));
            Check(g.First.Perforated==2&&g.First.Stopped==1&&g.First.SuccessfulResistance==300,"native event aggregation/dedup");
            q.Terminate(root,0);q.Terminate(root,0);q.FinishReady(20,false);Check(q.Count==0&&g.Active==1,"premature child close");
            q.Terminate(child,20);q.FinishReady(20,false);Check(q.Count==0,"settle grace absent");q.FinishReady(21,false);
            var r=q.Dequeue();Check(r.Complete&&r.ArmorContacts==3&&r.LastResistance==500,"final native summary");
            Check(q.Register(root,new object())==null,"late child duplicated sealed summary");q.FinishReady(30,true);Check(q.Count==0,"duplicate scene row");
            root=new object();g=q.Register(root,root);q.Terminate(root,0);q.FinishReady(20,false);Check(q.Count==0,"pure miss emitted");
            root=new object();g=q.Register(root,root);q.BeginHit(g);q.Add(g,Hit());q.FinishReady(20,true);Check(!q.Dequeue().Complete,"scene cutoff not incomplete");
            root=new object();g=q.Register(root,root);q.BeginHit(g);
            for(int i=0;i<2000;i++)q.Add(g,Hit(i));
            Check(g.Events.Count==256&&g.First.ArmorContacts==256&&g.Incomplete,"contact memory cap");q.FinishReady(20,true);q.Dequeue();
            q=new ShotAggregation();for(int i=0;i<100000;i++){root=new object();g=q.Register(root,root);Check(g!=null,"session cap early");q.Terminate(root,0);}
            Check(q.Register(new object(),new object())==null&&q.Claimed==100000,"session cap");
            q=new ShotAggregation();for(int i=0;i<1024;i++){root=new object();g=q.Register(root,root);Check(q.BeginHit(g),"pending cap early");q.Add(g,Hit(i));}
            root=new object();g=q.Register(root,root);Check(!q.BeginHit(g),"pending cap unbounded");q.FinishReady(20,true);Check(q.Count==1024,"force queue cap");
            q=new ShotAggregation();var weak=WeakFixture(q);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Check(!weak.IsAlive,"pending summary retained original shot graph");
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference WeakFixture(ShotAggregation q)
        {var root=new object();var g=q.Register(root,root);q.BeginHit(g);q.Add(g,Hit());return new WeakReference(root);}
        private static void Hooks()
        {
            MemoryStream memory;var mod=Activate(out memory);var round=new LiveRound();var c=Plate();Call("AfterInit",round);
            var ignored=Before(round,new Collider{name="TRACKING OBJECT"});Check(Row(ignored)==null,"tracking object accepted");
            Mod.ObserveDecision(round,999,1000);Mod.ObserveOutcome(round,1);EndContact(round,ignored);
            Check(Buffer(mod).Count==0,"tracking record generated");
            var state=Before(round,c);Check(Row(state)!=null,"armor rejected");Mod.ObserveDecision(round,320,700);Mod.ObserveOutcome(round,1);
            var nested=Before(round,new Collider());Mod.ObserveDecision(round,999,999);Mod.ObserveOutcome(round,2);EndContact(round,nested);
            Check(Row(state).DecisionResistance==320&&Row(state).Outcome=="perforated","ignored nested context contaminated outer");
            round.CurrentSpeed=1200;round.FixturePen=380;EndContact(round,state,false);
            state=Before(round,Plate("back plate"));Mod.ObserveDecision(round,500,380);Mod.ObserveOutcome(round,2);EndContact(round,state,true);
            Check(Buffer(mod).Count==0,"summary before termination");Call("BeforeTerminated",round.ShotInfo);Time.time=1;Time.realtimeSinceStartup=1;
            MelonLoader.MelonLogger.Messages.Clear();mod.OnUpdate();Check(Buffer(mod).Count==0&&MelonLoader.MelonLogger.Messages.Count==1,"one shot one console row");
            var console=MelonLoader.MelonLogger.Messages[0];Check(console.Contains("已穿透：部件“plate”×1次")&&console.Contains("未穿透：部件“back plate”×1次"),"branch results not aggregated");
            Check(round.CurrentSpeed==1200&&round.FixturePen==380,"observer mutated projectile");
            Check(Encoding.UTF8.GetString(memory.ToArray()).Split('\n').Length==3,"CSV rows");
            round=new LiveRound();Call("AfterInit",round);Call("BeforeTerminated",round.ShotInfo);Time.time=2;Time.realtimeSinceStartup=2;mod.OnUpdate();Check(MelonLoader.MelonLogger.Messages.Count==1,"miss console row");
            round=new LiveRound();round.ShotInfo.IsPlayerShot=false;state=Before(round,c);Check(Row(state)==null,"AI accepted");EndContact(round,state);
            round.ShotInfo.IsPlayerShot=true;round.IsSpall=true;state=Before(round,c);Check(Row(state)==null,"spall accepted");EndContact(round,state);round.IsSpall=false;
            round.Pooled=true;state=Before(round,c);Check(Row(state)==null,"pooled accepted");EndContact(round,state);round.Pooled=false;
            c.Tag="Detection";c.Components.Add(new GHPC.Equipment.ShotDetectionZone());state=Before(round,c);Check(Row(state)==null,"detection accepted");EndContact(round,state);
            c=Plate();((ArmorFixture)c.Components[0]).Unit=round.Shooter;state=Before(round,c);Check(Row(state)==null,"self accepted");EndContact(round,state);
            round.ShotInfo.ParentShot=round.ShotInfo;state=Before(round,Plate());Check(Row(state)==null,"cyclic ancestry");EndContact(round,state);
            round=new LiveRound();state=Before(round,new TerrainCollider());Check(Row(state)!=null,"terrain rejected");EndContact(round,state);Call("BeforeTerminated",round.ShotInfo);
            Time.time=3;Time.realtimeSinceStartup=3;mod.OnUpdate();Check(MelonLoader.MelonLogger.Messages.Last().Contains("不计算装甲穿透"),"terrain incorrectly claimed perforation");
            round=new LiveRound();state=Before(round,Plate());Call("FinalContact",state,new Exception("native"));Check(typeof(Mod).GetField("contact",Hidden).GetValue(null)==null,"exception context retained");
            mod.OnDeinitializeMelon();Check(typeof(Mod).GetField("owner",Hidden).GetValue(null)==null,"owner retained");
            mod=Activate(out memory);c=Plate();c.Throw=true;state=Before(new LiveRound(),c);Call("FinalContact",state,null);Check((bool)typeof(Mod).GetField("disabled",Hidden).GetValue(mod),"observer fault isolation");mod.OnDeinitializeMelon();
            mod=new Mod();mod.OnInitializeMelon();Check((bool)typeof(Mod).GetField("disabled",Hidden).GetValue(mod),"unknown native module accepted");
        }
        private static void TranspilerExecution()
        {
            // Executable CLR fixture with the audited native comparison layout.
            var method=new DynamicMethod("native_fixture",typeof(void),new[]{typeof(LiveRound),typeof(IArmor)},typeof(Mod),true);
            var il=method.GetILGenerator();var locals=new LocalBuilder[16];
            for(int i=0;i<16;i++)locals[i]=il.DeclareLocal(i==1?typeof(IArmor):typeof(float));
            var passed=il.DefineLabel();var code=new List<CodeInstruction>{
                new CodeInstruction(OpCodes.Ldarg_1),new CodeInstruction(OpCodes.Stloc_1),
                new CodeInstruction(OpCodes.Ldc_R4,320f),new CodeInstruction(OpCodes.Stloc_S,locals[14]),
                new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Call,typeof(LiveRound).GetProperty("CurrentPenRating").GetGetMethod()),
                new CodeInstruction(OpCodes.Stloc_S,locals[15]),new CodeInstruction(OpCodes.Ldloc_S,locals[14]),new CodeInstruction(OpCodes.Ldloc_S,locals[15]),new CodeInstruction(OpCodes.Ble_Un,passed),
                new CodeInstruction(OpCodes.Nop),new CodeInstruction(OpCodes.Ret),new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Callvirt,typeof(IArmor).GetMethod("NotifyPunctured")),new CodeInstruction(OpCodes.Ret)};
            code[12].labels.Add(passed);
            var ricochet=typeof(LiveRound).GetMethod("doRicochet",Hidden);
            for(int i=0;i<2;i++){code.Add(new CodeInstruction(OpCodes.Ldarg_0));code.Add(new CodeInstruction(OpCodes.Call,ricochet));}
            code.Add(new CodeInstruction(OpCodes.Nop));code.Add(new CodeInstruction(OpCodes.Nop));code.Add(new CodeInstruction(OpCodes.Ret));
            var original=code.ToArray();var patched=NativeObservation.Transpile(code).ToList();
            Check(patched.Count==original.Length+16,"observer insertion size");
            Check(patched.Where(c=>original.Contains(c)).SequenceEqual(original),"original instructions replaced/reordered");
            foreach(var c in patched)
            {
                foreach(var label in c.labels)il.MarkLabel(label);
                if(c.operand==null)il.Emit(c.opcode);
                else if(c.operand is LocalBuilder)il.Emit(c.opcode,(LocalBuilder)c.operand);
                else if(c.operand is Label)il.Emit(c.opcode,(Label)c.operand);
                else if(c.operand is MethodInfo)il.Emit(c.opcode,(MethodInfo)c.operand);
                else if(c.operand is float)il.Emit(c.opcode,(float)c.operand);
                else if(c.operand is int)il.Emit(c.opcode,(int)c.operand);
                else throw new Exception("unsupported fixture operand");
            }
            var execute=(Action<LiveRound,IArmor>)method.CreateDelegate(typeof(Action<LiveRound,IArmor>));
            MemoryStream memory;var mod=Activate(out memory);var round=new LiveRound();var plate=new ArmorFixture();var collider=new Collider();collider.Components.Add(plate);
            var state=Before(round,collider);execute(round,plate);Check(plate.Punctures==1&&Row(state).Outcome=="perforated"&&Row(state).DecisionResistance==320&&Row(state).DecisionPen==700,"native success branch fixture");EndContact(round,state);
            round=new LiveRound{FixturePen=200};state=Before(round,collider);execute(round,plate);Check(plate.Punctures==1&&Row(state).Outcome=="not_perforated"&&Row(state).DecisionPen==200,"native failure branch fixture");EndContact(round,state);
            mod.OnDeinitializeMelon();
            bool refused=false;try{NativeObservation.Transpile(new CodeInstruction[0]).ToList();}catch(InvalidOperationException){refused=true;}Check(refused,"unknown IL not refused");
        }
        private static bool HasCjk(string text){foreach(char c in text)if(c>='\u4e00'&&c<='\u9fff')return true;return false;}
        private static void Languages()
        {
            var zh=new CultureInfo("zh-CN");var en=new CultureInfo("en-US");
            foreach(string value in new[]{"zh","ZH","zh-cn","chinese"," Chinese "})Check(!LanguageSettings.IsEnglish(value,en),"Chinese override not respected");
            foreach(string value in new[]{"en","EN","english"," English "})Check(LanguageSettings.IsEnglish(value,zh),"English override not respected");
            Check(!LanguageSettings.IsEnglish("auto",zh)&&!LanguageSettings.IsEnglish(null,new CultureInfo("zh-TW")),"auto Chinese language");
            Check(LanguageSettings.IsEnglish("auto",en)&&LanguageSettings.IsEnglish("auto",new CultureInfo("de-DE")),"non-Chinese auto fallback");
            Check(LanguageSettings.IsEnglish("unknown",en)&&!LanguageSettings.IsEnglish("unknown",zh),"invalid setting does not fall back to system");
            LanguageSettings.Initialize();
            var entry=(MelonLoader.MelonPreferences_Entry<string>)typeof(LanguageSettings).GetField("language",Hidden).GetValue(null);
            Check(entry.Value=="zh","default preference is not Chinese");
            var previousCulture=CultureInfo.CurrentUICulture;
            try{CultureInfo.CurrentUICulture=en;Check(!LanguageSettings.English,"default uses English on an English UI culture");entry.Value="auto";Check(LanguageSettings.English,"explicit auto no longer follows English UI culture");}
            finally{CultureInfo.CurrentUICulture=previousCulture;entry.Value="zh";}
            var q=new ShotAggregation();var root=new object();var g=q.Register(root,root);q.BeginHit(g);
            var first=Hit(1);first.Surface="Kontakt-5";first.Ammo="M829A2";first.Range=100;first.Obliquity=60;first.SpeedBefore=1500;
            q.Add(g,first);var last=Hit(2,"not_perforated",500);last.Surface="hull floor";last.DecisionPen=200;last.PenAfter=float.NaN;q.Add(g,last);
            q.Terminate(root,0);q.FinishReady(1,false);var row=q.Dequeue();
            entry.Value="zh";string chinese=RecordRules.Summary(row),csv=RecordRules.Csv(row);
            entry.Value="en";string english=RecordRules.Summary(row);
            Check(chinese.Contains("车底板")&&chinese.Contains("已穿透：接触-5反应装甲"),"Chinese output regressed");
            Check(english.Contains("Stage / Component | Result | Native readings")&&english.Contains("Kontakt-5")&&english.Contains("hull floor (Not perforated)"),"English table/name/outcome missing");
            Check(english.Contains("range=100 m")&&english.Contains("obliquity=60 deg")&&english.Contains("speed=1500 m/s")&&english.Contains("after contact=not recorded"),"English units or missing values");
            Check(!HasCjk(english)&&!english.Contains("原名，未翻译")&&!english.Contains("说明："),"Chinese/explanatory text leaked into English table");
            Check(csv==RecordRules.Csv(row),"display language changed CSV data/schema");
            Check(RecordRules.Summary(new HitRecord{Kind="terrain",Complete=true}).Contains("Terrain contact only; armor penetration: N/A"),"English terrain verdict");
            Check(RecordRules.Summary(new HitRecord()).Contains("Incomplete record")&&RecordRules.Summary(new HitRecord()).Contains("range=not recorded"),"English incomplete/missing data");
            row.ArmorEvents.Add(new ArmorEvent("unknown raw part","unobserved"));row.ArmorContacts++;row.Unknown++;row.Complete=false;
            english=RecordRules.Summary(row);Check(english.Contains("unknown raw part")&&english.Contains("Unconfirmed"),"English unknown name/verdict lost");
            for(int i=0;i<1000;i++)
            {
                entry.Value=i%2==0?"zh":"en";string summary=RecordRules.Summary(row);
                Check((i%2==0)==summary.Contains("阶段 / 部件"),"language switch stale state");
                Check(RecordRules.Csv(row).Split('\n').Length==1,"language switch split CSV rows");
            }
            entry.Value="en";Console.WriteLine("ENGLISH_EXAMPLE "+RecordRules.Summary(row));
            entry.Value="zh";
        }
        public static int Main()
        {
            var originalCulture=CultureInfo.CurrentUICulture;
            try{CultureInfo.CurrentUICulture=new CultureInfo("zh-CN");Rules();DisplayRules();Aggregate();Hooks();TranspilerExecution();Languages();Console.WriteLine("PASS checks="+checks+" language_auto_zh_en=true chinese_part_outcomes=true full_csv_event_names=true native_branch_fixture=true one_aggregate_per_original_shot=true miss_no_row=true tracking_rejected=true weak_identity=true bounded=true native_fields_unchanged=true live_safety_not_verified=true self_damage_not_fixed=true");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{CultureInfo.CurrentUICulture=originalCulture;}
        }
    }
}

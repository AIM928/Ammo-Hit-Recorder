using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Runtime.CompilerServices;
using MelonLoader;

namespace AIMAmmoHitRecorder
{
    internal static class LanguageSettings
    {
        private static MelonPreferences_Entry<string> language;
        internal static void Initialize()
        {
            var category=MelonPreferences.CreateCategory("AIMAmmoHitRecorder","Ammo Hit Recorder");
            language=category.CreateEntry<string>("Language","zh","Display language",
                "auto = system UI language; zh = Chinese; en = English. Restart the game after editing this setting.");
        }
        internal static bool IsEnglish(string value,CultureInfo culture)
        {
            string code=(value??"auto").Trim().ToLowerInvariant();
            if(code=="en"||code=="english")return true;
            if(code=="zh"||code=="zh-cn"||code=="chinese")return false;
            return culture==null||culture.TwoLetterISOLanguageName!="zh";
        }
        internal static bool English{get{return IsEnglish(language==null?"zh":language.Value,CultureInfo.CurrentUICulture);}}
        internal static string Choose(string chinese,string english){return English?english:chinese;}
    }
    // Managed scalar/string snapshots only; no collider, vehicle or native object retained.
    internal sealed class ArmorEvent
    {
        internal readonly string Surface,Outcome;
        internal readonly float DecisionPen=float.NaN,Resistance=float.NaN,PenAfter=float.NaN;
        internal readonly int Round;
        internal readonly bool Child;
        internal ArmorEvent(string surface,string outcome){Surface=surface;Outcome=outcome;}
        internal ArmorEvent(HitRecord row):this(row.Surface,row.Outcome)
        {DecisionPen=row.DecisionPen;Resistance=row.DecisionResistance;PenAfter=row.PenAfter;Round=row.Round;Child=row.Child;}
    }
    internal sealed class HitRecord
    {
        internal float Time,SpeedBefore,PenBefore,BasePen,SpeedAfter=float.NaN,PenAfter=float.NaN,X,Y,Z;
        internal int Round,Collider,Frame;
        internal object ShotKey;
        internal string Ammo,Surface,Kind;
        internal double Obliquity=double.NaN;
        internal double Range=double.NaN;
        internal float StartX=float.NaN,StartY=float.NaN,StartZ=float.NaN;
        internal bool Continue,Child;
        internal float DecisionResistance=float.NaN,DecisionPen=float.NaN;
        internal string Outcome="unobserved",FirstArmor,LastArmor,LastOutcome="unobserved",Finalization;
        internal float LastResistance=float.NaN,LastDecisionPen=float.NaN,LastArmorPenAfter=float.NaN;
        internal int ArmorContacts,TerrainContacts,Perforated,Stopped,Ricochets,Unknown;
        internal double SuccessfulResistance;
        internal bool Complete;
        internal readonly List<ArmorEvent> ArmorEvents=new List<ArmorEvent>();
    }
    internal static class RecordRules
    {
        internal const string Header="game_time_s,round_id,ammo,surface,surface_kind,obliquity_deg_0_normal,surface_angle_deg_90_normal,speed_contact_entry_mps,pen_contact_entry_mmRHA,ammo_base_pen_mmRHA,speed_after_mps,pen_after_mmRHA,native_continue,child_projectile,hit_x_m,hit_y_m,hit_z_m,straight_range_m,launch_x_m,launch_y_m,launch_z_m,cos_projected_normal_pen_mmRHA,armor_contacts,perforated_events,not_perforated_events,ricochet_events,unobserved_armor_events,successful_native_los_resistance_sum_mmRHAe,first_armor,last_armor,last_armor_outcome,last_armor_resistance_mmRHAe,last_armor_decision_pen_mmRHA,last_armor_pen_after_mmRHA,summary_complete,finalization,armor_event_details_zh";
        internal static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        internal static double Angle(double dx,double dy,double dz,double nx,double ny,double nz)
        {
            double d2=dx*dx+dy*dy+dz*dz,n2=nx*nx+ny*ny+nz*nz;
            if(!Finite(d2)||!Finite(n2)||d2<1e-16||n2<1e-16)return double.NaN;
            double cosine=-(dx*nx+dy*ny+dz*nz)/Math.Sqrt(d2*n2);
            return Math.Acos(Math.Max(-1,Math.Min(1,cosine)))*180/Math.PI;
        }
        internal static string Number(double n){return Finite(n)?n.ToString("0.###",CultureInfo.InvariantCulture):"";}
        internal static double Range(double sx,double sy,double sz,double x,double y,double z)
        {
            double dx=x-sx,dy=y-sy,dz=z-sz,d2=dx*dx+dy*dy+dz*dz;
            return Finite(d2)?Math.Sqrt(d2):double.NaN;
        }
        private static string Display(double n){return Finite(n)?n.ToString("0.##",CultureInfo.InvariantCulture):"未记录";}
        private static string Measured(double n,string unit){return Finite(n)?Display(n)+unit:"未记录";}
        internal static double NormalPen(double currentPen,double obliquity)
        {
            if(!Finite(currentPen)||!Finite(obliquity)||currentPen<0||obliquity<0||obliquity>90)return double.NaN;
            if(obliquity==90)return 0;
            return currentPen*Math.Cos(obliquity*Math.PI/180);
        }
        internal static string Summary(HitRecord r)
        {return LanguageSettings.English?EnglishSummary(r):ChineseSummary(r);}
        private static string ChineseSummary(HitRecord r)
        {
            var text=new StringBuilder("[AmmoHit] #"+r.Round+" "+Clean(r.Ammo,128));
            text.Append("\n初次接触：距离=").Append(Measured(r.Range,"米")).Append("，入射角=").Append(Measured(r.Obliquity,"°（0°为正撞）"))
                .Append("，速度=").Append(Measured(r.SpeedBefore,"米/秒"));
            if(r.ArmorContacts==0)
                text.Append(r.Kind=="terrain"||r.TerrainContacts>0?"\n只记录到命中地面；不计算装甲穿透":"\n未记录到装甲判定");
            else
            {
                text.Append("\n阶段 / 部件 | 判定 | 原生读数");
                text.Append("\n首次有效接触前 | — | 穿透能力=").Append(Measured(r.PenBefore,"mmRHA"));
                int shown=Math.Min(12,r.ArmorEvents.Count);
                for(int i=0;i<shown;i++)
                {
                    var item=r.ArmorEvents[i];
                    text.Append('\n').Append(i+1).Append(". ").Append(ArmorName(item.Surface)).Append(" [弹丸#").Append(item.Round)
                        .Append(item.Child?"，子弹丸]":"]").Append(" | ").Append(OutcomeText(item.Outcome)).Append(" | ").Append(StageReadings(item));
                }
                if(r.ArmorEvents.Count>shown)text.Append("\n另有").Append(r.ArmorEvents.Count-shown).Append("次接触未展开，完整阶段表见CSV第37列。");
                if(r.ArmorEvents.Count<r.ArmorContacts)text.Append("\n部分接触未保存阶段明细，不能补算中间剩余能力。");
                text.Append("\n最后装甲接触：").Append(ArmorName(r.LastArmor)).Append("（").Append(OutcomeText(r.LastOutcome))
                    .Append("）；判定前剩余能力=").Append(Measured(r.LastDecisionPen,"mmRHA")).Append("，该处等效阻力=").Append(Measured(r.LastResistance,"mmRHAe"));
                text.Append("\n已穿透：").Append(Parts(r,"perforated",r.Perforated)).Append("；未穿透：").Append(Parts(r,"not_perforated",r.Stopped))
                    .Append("；跳弹：").Append(Parts(r,"ricochet",r.Ricochets)).Append("；未确认：").Append(Parts(r,"unobserved",r.Unknown));
            }
            if(!r.Complete)text.Append("\n记录不完整，仅列出已观察到的结果。");
            return text.ToString();
        }
        private static string EnglishMeasured(double n,string unit){return Finite(n)?Display(n)+unit:"not recorded";}
        private static string EnglishName(string name){string value=Clean(name,128);return value.Length==0?"unnamed component":value;}
        private static string EnglishOutcome(string outcome)
        {return outcome=="perforated"?"Perforated":outcome=="not_perforated"?"Not perforated":outcome=="ricochet"?"Ricochet":"Unconfirmed";}
        private static string EnglishStage(ArmorEvent item)
        {
            return "Before comparison="+EnglishMeasured(item.DecisionPen," mmRHA")+"; resistance="+EnglishMeasured(item.Resistance," mmRHAe")+
                (item.Outcome=="perforated"?"; remaining after perforation=":"; after contact=")+EnglishMeasured(item.PenAfter," mmRHA");
        }
        private static string EnglishParts(HitRecord r,string outcome,int count)
        {
            if(count==0)return "none recorded";
            var names=new List<string>();var counts=new List<int>();int observed=0;
            foreach(var item in r.ArmorEvents)
            {
                if(NormalOutcome(item.Outcome)!=outcome)continue;
                observed++;string name=EnglishName(item.Surface);int index=names.IndexOf(name);
                if(index<0){names.Add(name);counts.Add(1);}else counts[index]++;
            }
            if(observed==0)return count+" contacts (names not saved)";
            var parts=new List<string>();
            for(int i=0;i<Math.Min(4,names.Count);i++)parts.Add(names[i]+" x"+counts[i]);
            if(names.Count>4)parts.Add("+"+(names.Count-4)+" component names; full details in CSV");
            if(observed<count)parts.Add((count-observed)+" contacts without saved names");
            return string.Join(", ",parts.ToArray());
        }
        private static string EnglishSummary(HitRecord r)
        {
            var text=new StringBuilder("[AmmoHit] #"+r.Round+" "+Clean(r.Ammo,128));
            text.Append("\nFirst contact: range=").Append(EnglishMeasured(r.Range," m")).Append(", obliquity=")
                .Append(EnglishMeasured(r.Obliquity," deg (0 = normal impact)")).Append(", speed=").Append(EnglishMeasured(r.SpeedBefore," m/s"));
            if(r.ArmorContacts==0)
                text.Append(r.Kind=="terrain"||r.TerrainContacts>0?"\nTerrain contact only; armor penetration: N/A":"\nNo armor decision recorded");
            else
            {
                text.Append("\nStage / Component | Result | Native readings");
                text.Append("\nBefore first accepted contact | - | Capability=").Append(EnglishMeasured(r.PenBefore," mmRHA"));
                int shown=Math.Min(12,r.ArmorEvents.Count);
                for(int i=0;i<shown;i++)
                {
                    var item=r.ArmorEvents[i];
                    text.Append('\n').Append(i+1).Append(". ").Append(EnglishName(item.Surface)).Append(" [projectile #").Append(item.Round)
                        .Append(item.Child?", child]":"]").Append(" | ").Append(EnglishOutcome(item.Outcome)).Append(" | ").Append(EnglishStage(item));
                }
                if(r.ArmorEvents.Count>shown)text.Append("\n").Append(r.ArmorEvents.Count-shown).Append(" more contacts; full stages in CSV column 37.");
                if(r.ArmorEvents.Count<r.ArmorContacts)text.Append("\nSome stage details were not saved; intermediate capability cannot be reconstructed.");
                text.Append("\nLast armor contact: ").Append(EnglishName(r.LastArmor)).Append(" (").Append(EnglishOutcome(r.LastOutcome))
                    .Append("); before comparison=").Append(EnglishMeasured(r.LastDecisionPen," mmRHA")).Append(", resistance=").Append(EnglishMeasured(r.LastResistance," mmRHAe"));
                text.Append("\nPerforated: ").Append(EnglishParts(r,"perforated",r.Perforated)).Append("; Not perforated: ").Append(EnglishParts(r,"not_perforated",r.Stopped))
                    .Append("; Ricochet: ").Append(EnglishParts(r,"ricochet",r.Ricochets)).Append("; Unconfirmed: ").Append(EnglishParts(r,"unobserved",r.Unknown));
            }
            if(!r.Complete)text.Append("\nIncomplete record; observed results only.");
            return text.ToString();
        }
        private static string StageReadings(ArmorEvent item)
        {
            return "判定前能力="+Measured(item.DecisionPen,"mmRHA")+"；该处阻力="+Measured(item.Resistance,"mmRHAe")+
                (item.Outcome=="perforated"?"；穿透后剩余能力=":"；处理后能力=")+Measured(item.PenAfter,"mmRHA");
        }
        private static string Parts(HitRecord r,string outcome,int count)
        {
            if(count==0)return "无记录";
            var names=new List<string>();var counts=new List<int>();int observed=0;
            foreach(var item in r.ArmorEvents)
            {
                if(NormalOutcome(item.Outcome)!=outcome)continue;
                observed++;string name=ArmorName(item.Surface);int index=names.IndexOf(name);
                if(index<0){names.Add(name);counts.Add(1);}else counts[index]++;
            }
            if(observed==0)return count+"次（部件名称未保存）";
            var parts=new List<string>();
            for(int i=0;i<Math.Min(4,names.Count);i++)parts.Add(names[i]+"×"+counts[i]+"次");
            if(names.Count>4)parts.Add("另"+(names.Count-4)+"种部件，完整明细见CSV");
            if(observed<count)parts.Add((count-observed)+"次部件名称未保存");
            return string.Join("、",parts.ToArray());
        }
        private static string NormalOutcome(string value)
        {return value=="perforated"||value=="not_perforated"||value=="ricochet"?value:"unobserved";}
        internal static string ArmorName(string raw)
        {
            string name=Clean(raw,128);string translated;
            switch(name.Trim().ToLowerInvariant())
            {
                case "kontakt-1":case "kontakt1":translated="接触-1反应装甲";break;
                case "kontakt-5":case "kontakt5":translated="接触-5反应装甲";break;
                case "relikt":translated="化石反应装甲";break;
                case "glacis splash guard":translated="首上挡浪板";break;
                case "hull floor":translated="车底板";break;
                case "hull roof":translated="车体顶板";break;
                case "hull side":translated="车体侧板";break;
                case "hull rear plate":translated="车体后装甲板";break;
                case "lower front plate":translated="首下装甲板";break;
                case "upper front plate":translated="首上装甲板";break;
                case "glacis front plate":translated="首上外装甲板";break;
                case "glacis backing plate":translated="首上背板";break;
                case "glacis applique plate":translated="首上附加装甲板";break;
                case "turret roof":translated="炮塔顶板";break;
                case "turret":translated="炮塔";break;
                case "turret cheek face":translated="炮塔正面外板";break;
                case "turret cheek backing plate":translated="炮塔正面背板";break;
                case "turret rear face":translated="炮塔后装甲板";break;
                case "turret side face":translated="炮塔侧装甲板";break;
                case "special armor array":translated="特殊装甲阵列";break;
                case "main gun mount":translated="主炮安装座";break;
                case "gun mantlet":translated="炮盾";break;
                case "gun barrel":translated="炮管";break;
                case "gun breech":translated="炮闩";break;
                case "gun breech housing":translated="炮尾壳体";break;
                case "firewall":translated="发动机防火隔板";break;
                case "right track bank":case "right track":translated="右侧履带组";break;
                case "left track bank":case "left track":translated="左侧履带组";break;
                case "road wheel":translated="负重轮";break;
                case "final drive":translated="侧减速器";break;
                case "final drive armor":translated="侧减速器防护板";break;
                case "engine":translated="发动机";break;
                case "fuel tank":translated="油箱";break;
                case "driver's seat":translated="驾驶员座椅";break;
                case "side skirt":translated="侧裙板";break;
                case "mud flap":translated="挡泥板";break;
                case "rubber flap":translated="橡胶挡板";break;
                case "sponson":translated="车体侧舱";break;
                case "ramp":translated="跳板";break;
                case "ir spotlight cover":translated="红外探照灯护罩";break;
                case "propellant casing":translated="发射药壳体";break;
                case "long rod penetrator":translated="长杆穿甲体";break;
                default:return name.Length==0?"部件名称未记录":"部件“"+name+"”";
            }
            return translated;
        }
        private static string EventDetails(HitRecord r)
        {
            var text=new StringBuilder();int index=0;
            foreach(var item in r.ArmorEvents)
            {
                if(index>0)text.Append("；");
                text.Append(++index).Append(". ").Append(ArmorName(item.Surface)).Append("（原名：").Append(Clean(item.Surface,128))
                    .Append("；弹丸#").Append(item.Round).Append(item.Child?"，子弹丸）":"）").Append("：").Append(OutcomeText(item.Outcome))
                    .Append("；").Append(StageReadings(item));
            }
            // At most 256 events, each with a 128-character source label. Keep the
            // full bounded details field rather than Text()'s single-name limit.
            return "\""+text.ToString().Replace("\"","\"\"")+"\"";
        }
        private static string OutcomeText(string outcome)
        {return outcome=="perforated"?"已穿透":outcome=="not_perforated"?"未穿透":outcome=="ricochet"?"跳弹":"未确认，未观察到完整判定";}
        private static string Clean(string text,int limit)
        {
            var b=new StringBuilder();
            foreach(char c in text??""){if(b.Length>=limit)break;b.Append(char.IsControl(c)?' ':c);}
            return b.ToString();
        }
        internal static string Text(string text)
        {
            text=text??"";var b=new StringBuilder();
            foreach(char c in text){if(b.Length>=128)break;b.Append(char.IsControl(c)?' ':c);}
            text=b.ToString();string trimmed=text.TrimStart();
            if(trimmed.Length>0&&"=+-@".IndexOf(trimmed[0])>=0)text="'"+text;
            return "\""+text.Replace("\"","\"\"")+"\"";
        }
        internal static string Csv(HitRecord r)
        {
            return string.Join(",",new[]{Number(r.Time),r.Round.ToString(CultureInfo.InvariantCulture),Text(r.Ammo),Text(r.Surface),Text(r.Kind),
                Number(r.Obliquity),Number(90-r.Obliquity),Number(r.SpeedBefore),Number(r.PenBefore),Number(r.BasePen),Number(r.SpeedAfter),Number(r.PenAfter),
                r.Continue?"true":"false",r.Child?"true":"false",Number(r.X),Number(r.Y),Number(r.Z),Number(r.Range),Number(r.StartX),Number(r.StartY),Number(r.StartZ),Number(NormalPen(r.PenBefore,r.Obliquity)),
                Number(r.ArmorContacts),Number(r.Perforated),Number(r.Stopped),Number(r.Ricochets),Number(r.Unknown),r.ArmorContacts==0?"":Number(r.SuccessfulResistance),
                Text(r.FirstArmor),Text(r.LastArmor),Text(r.LastOutcome),Number(r.LastResistance),Number(r.LastDecisionPen),Number(r.LastArmorPenAfter),r.Complete?"true":"false",Text(r.Finalization),EventDetails(r)});
        }
    }
    internal sealed class RecordBuffer
    {
        internal const int MaxQueued=1024,MaxSession=100000;
        private sealed class Seen {internal bool Submitted;}
        // Weak identity keys never hold AAR / vehicle graphs alive. Unlike an ID
        // eviction cache, a still-live old shot cannot become eligible again.
        private ConditionalWeakTable<object,Seen> shots=new ConditionalWeakTable<object,Seen>();
        private readonly Queue<HitRecord> queue=new Queue<HitRecord>();
        internal int Accepted,Dropped,Duplicates,Claimed;
        internal int Count{get{return queue.Count;}}
        internal bool HasSeen(object key){Seen seen;return key!=null&&shots.TryGetValue(key,out seen);}
        internal bool TryClaim(object key)
        {
            if(key==null){Dropped++;return false;}
            if(HasSeen(key)){Duplicates++;return false;}
            if(Claimed>=MaxSession){Dropped++;return false;}
            shots.Add(key,new Seen());Claimed++;return true;
        }
        internal bool Enqueue(HitRecord r)
        {
            Seen seen;
            if(r.ShotKey==null||!shots.TryGetValue(r.ShotKey,out seen)){Dropped++;return false;}
            if(seen.Submitted){Duplicates++;return false;}
            seen.Submitted=true;
            if(queue.Count>=MaxQueued){Dropped++;return false;}
            queue.Enqueue(r);Accepted++;return true;
        }
        internal HitRecord Dequeue(){return queue.Dequeue();}
        // Keep weak dedup state across scene boundaries in case an old shot is
        // observed late. No scene or Unity object traversal is needed.
        internal void ClearScene(){}
        internal void Discard(){queue.Clear();shots=new ConditionalWeakTable<object,Seen>();}
    }
}

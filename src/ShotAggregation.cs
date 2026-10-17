using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AIMAmmoHitRecorder
{
    internal sealed class ShotAggregation
    {
        internal const int MaxQueued=1024,MaxSession=100000,MaxContacts=256;
        internal sealed class Group
        {
            internal int Active,Contacts;
            internal bool Finished,Tracked,Incomplete;
            internal float EndTime;
            internal HitRecord First;
            internal readonly HashSet<string> Events=new HashSet<string>();
        }
        private sealed class Participant{internal Group Group;internal bool Ended;}
        private ConditionalWeakTable<object,Group> roots=new ConditionalWeakTable<object,Group>();
        private ConditionalWeakTable<object,Participant> participants=new ConditionalWeakTable<object,Participant>();
        private readonly List<Group> pending=new List<Group>();
        private readonly Queue<HitRecord> queue=new Queue<HitRecord>();
        internal int Claimed,Dropped,Accepted;
        internal int Count{get{return queue.Count;}}
        internal Group Register(object root,object shot)
        {
            if(root==null||shot==null)return null;
            Participant p;if(participants.TryGetValue(shot,out p))return p.Group.Finished||p.Ended?null:p.Group;
            Group g;
            if(!roots.TryGetValue(root,out g))
            {
                if(Claimed>=MaxSession){Dropped++;return null;}
                roots.Add(root,g=new Group());Claimed++;
            }
            if(g.Finished)return null;
            participants.Add(shot,new Participant{Group=g});g.Active++;return g;
        }
        internal void Terminate(object shot,float time)
        {
            Participant p;
            if(shot!=null&&participants.TryGetValue(shot,out p)&&!p.Ended)
            {p.Ended=true;p.Group.Active--;p.Group.EndTime=time;}
        }
        internal bool BeginHit(Group g)
        {
            if(g==null||g.Finished)return false;
            if(!g.Tracked)
            {
                if(pending.Count+queue.Count>=MaxQueued){g.Finished=true;Dropped++;return false;}
                g.Tracked=true;pending.Add(g);
            }
            return true;
        }
        internal void MarkIncomplete(Group g){if(g!=null)g.Incomplete=true;}
        internal void Add(Group g,HitRecord row)
        {
            if(g==null||g.Finished)return;
            if(g.First==null)g.First=row;
            // Keep first physical hit for speed/range; armor results are accumulated
            // independently, so later armor after terrain is not mislabeled terrain.
            var first=g.First;
            if(row.Kind=="terrain"){if(first.TerrainContacts<MaxContacts)first.TerrainContacts++;return;}
            // Duplicate native callbacks for exactly the same projectile, plate and
            // point must not inflate the successful LOS-resistance event sum.
            string key=row.Round+":"+row.Collider+":"+row.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+
                row.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+row.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            if(g.Events.Contains(key))return;
            if(g.Contacts>=MaxContacts){g.Incomplete=true;return;}
            g.Events.Add(key);g.Contacts++;
            first.ArmorEvents.Add(new ArmorEvent(row));
            first.ArmorContacts++;
            if(first.FirstArmor==null)first.FirstArmor=row.Surface;
            first.LastArmor=row.Surface;first.LastOutcome=row.Outcome;
            first.LastResistance=row.DecisionResistance;first.LastDecisionPen=row.DecisionPen;
            first.LastArmorPenAfter=row.PenAfter;
            if(row.Outcome=="perforated")
            {
                first.Perforated++;
                if(RecordRules.Finite(row.DecisionResistance)&&row.DecisionResistance>=0)first.SuccessfulResistance+=row.DecisionResistance;
                else g.Incomplete=true;
            }
            else if(row.Outcome=="not_perforated")first.Stopped++;
            else if(row.Outcome=="ricochet")first.Ricochets++;
            else first.Unknown++;
        }
        internal void FinishReady(float time,bool force)
        {
            for(int i=pending.Count-1;i>=0;i--)
            {
                var g=pending[i];
                // Wait for native termination, not an arbitrary impact timer.
                if(!force&&(g.Active!=0||time-g.EndTime<.05f))continue;
                g.Finished=true;pending.RemoveAt(i);
                if(g.First==null)continue;
                g.First.Complete=!g.Incomplete&&g.Active==0&&!force&&g.First.Unknown==0;
                g.First.Finalization=force?"scene_or_unload": "native_termination";
                g.First.ShotKey=null;
                g.Events.Clear();
                if(queue.Count>=MaxQueued){Dropped++;continue;}
                queue.Enqueue(g.First);Accepted++;
            }
        }
        internal HitRecord Dequeue(){return queue.Dequeue();}
        internal void Discard(){pending.Clear();queue.Clear();roots=new ConditionalWeakTable<object,Group>();participants=new ConditionalWeakTable<object,Participant>();}
    }
}

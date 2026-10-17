using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    public sealed class HarmonyMethod{public int priority;public HarmonyMethod(Type type,string name){}}
    public static class Priority{public const int First=800,Last=0;}
    public sealed class Harmony{public int Removed;public void Patch(MethodBase original,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod transpiler=null,HarmonyMethod finalizer=null,HarmonyMethod ilmanipulator=null){}public void UnpatchSelf(){Removed++;}}
    public sealed class CodeInstruction
    {
        public System.Reflection.Emit.OpCode opcode;public object operand;
        public readonly List<System.Reflection.Emit.Label> labels=new List<System.Reflection.Emit.Label>();
        public readonly List<object> blocks=new List<object>();
        public CodeInstruction(System.Reflection.Emit.OpCode op,object arg=null){opcode=op;operand=arg;}
    }
}
namespace MelonLoader
{
    public static class MelonPreferences
    {public static MelonPreferences_Category CreateCategory(string identifier,string displayName){return new MelonPreferences_Category();}}
    public sealed class MelonPreferences_Category
    {
        public MelonPreferences_Entry<T> CreateEntry<T>(string identifier,T defaultValue,string displayName,string description,bool isHidden=false,bool dontSaveDefault=false,object validator=null,string oldIdentifier=null)
        {return new MelonPreferences_Entry<T>{Value=defaultValue};}
    }
    public sealed class MelonPreferences_Entry<T>{public T Value{get;set;}}
    [AttributeUsage(AttributeTargets.Assembly)]public sealed class MelonInfoAttribute:Attribute{public MelonInfoAttribute(Type t,string n,string v,string a){}}
    [AttributeUsage(AttributeTargets.Assembly)]public sealed class MelonGameAttribute:Attribute{public MelonGameAttribute(string a,string b){}}
    public class MelonMod
    {
        public HarmonyLib.Harmony HarmonyInstance=new HarmonyLib.Harmony();
        public virtual void OnInitializeMelon(){}public virtual void OnUpdate(){}public virtual void OnSceneWasUnloaded(int i,string s){}public virtual void OnDeinitializeMelon(){}
    }
    public static class MelonLogger{public static int Warnings;public static readonly List<string> Messages=new List<string>();public static void Msg(string s){Messages.Add(s);}public static void Warning(string s){Warnings++;}}
}
namespace MelonLoader.Utils{public static class MelonEnvironment{public static string UserDataDirectory="unused-by-adapter";}}
namespace UnityEngine
{
    public class Object
    {
        public string name="fixture";
        public static bool operator==(Object a,Object b){return ReferenceEquals(a,b);}public static bool operator!=(Object a,Object b){return !ReferenceEquals(a,b);}
        public override bool Equals(object o){return ReferenceEquals(this,o);}public override int GetHashCode(){return base.GetHashCode();}
    }
    public class Collider:Object
    {
        private static int next;public int FixtureId=++next;public string Tag="Untagged";public bool Throw;public readonly List<object> Components=new List<object>();
        public bool CompareTag(string tag){return Tag==tag;}public int GetInstanceID(){return FixtureId;}
        public T GetComponent<T>(){if(Throw)throw new InvalidOperationException("fixture");foreach(var c in Components)if(c is T)return (T)c;return default(T);}
    }
    public class TerrainCollider:Collider{}
    public struct Vector3{public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
    public static class Time{public static float time,realtimeSinceStartup;public static int frameCount;}
}
namespace GHPC
{
    public interface IUnit{}
    public class Unit:IUnit{}
    public interface IArmor{string Name{get;}IUnit Unit{get;}void NotifyPunctured();}
    public sealed class ArmorFixture:IArmor{public string FixtureName="test armor";public string Name{get{return FixtureName;}}public IUnit Unit{get;set;}public int Punctures;public void NotifyPunctured(){Punctures++;}}
}
namespace GHPC.Equipment{public sealed class ShotDetectionZone{}}
namespace GHPC.AI.Interfaces{public interface ITarget{}}
public sealed class AmmoType{public string Name="M829A2";public float RhaPenetration=730f;}
namespace GHPC.Weapons
{
    public sealed class ShotInfo{public ShotInfo ParentShot;public bool IsPlayerShot=true;public UnityEngine.Vector3 StartPosition;public void NotifyShotTerminated(){}}
    public sealed class LiveRound:UnityEngine.Object
    {
        public AmmoType Info=new AmmoType();public bool IsSpall,Pooled;public ShotInfo ShotInfo=new ShotInfo();public GHPC.Unit Shooter=new GHPC.Unit();
        public int ID=1;public float CurrentSpeed=1500,FixturePen=700;public float CurrentPenRating{get{return FixturePen;}}public LiveRound ParentRound;
        private bool penCheck(UnityEngine.Collider c,UnityEngine.Vector3 normal,UnityEngine.Vector3 path,UnityEngine.Vector3 point,GHPC.AI.Interfaces.ITarget target){return true;}
        private void doRicochet(){}
        public void Init(LiveRound parent,GHPC.IUnit shooter){}
    }
}

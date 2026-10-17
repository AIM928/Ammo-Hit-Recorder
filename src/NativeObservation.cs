using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GHPC.Weapons;
using HarmonyLib;

namespace AIMAmmoHitRecorder
{
    // Add only stack-neutral void observers. Never repeat a native armor call,
    // replace a branch/result, or store to native fields/locals/arguments.
    internal static class NativeObservation
    {
        private static bool Local(CodeInstruction c,int index,bool store)
        {
            if(c.opcode!=(store?OpCodes.Stloc_S:OpCodes.Ldloc_S)&&c.opcode!=(store?OpCodes.Stloc:OpCodes.Ldloc))return false;
            return c.operand is LocalBuilder&&((LocalBuilder)c.operand).LocalIndex==index;
        }
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();int decision=-1,passed=-1,failed=-1,ricochets=0;
            for(int i=1;i+3<code.Count;i++)
            {
                var method=code[i-1].operand as MethodInfo;
                if(Local(code[i],15,true)&&method!=null&&method.DeclaringType==typeof(LiveRound)&&method.Name=="get_CurrentPenRating"&&
                    Local(code[i+1],14,false)&&Local(code[i+2],15,false)&&code[i+3].opcode==OpCodes.Ble_Un)
                {
                    if(decision>=0)throw new InvalidOperationException("Ambiguous native armor comparison");
                    decision=i+1;failed=i+4;
                    var label=(Label)code[i+3].operand;
                    passed=code.FindIndex(c=>c.labels.Contains(label));
                }
            }
            ricochets=code.Count(c=>c.operand is MethodInfo&&((MethodInfo)c.operand).DeclaringType==typeof(LiveRound)&&((MethodInfo)c.operand).Name=="doRicochet");
            if(decision<0||passed<0||failed<0||ricochets!=2||code.Any(c=>c.blocks.Count!=0))
                throw new InvalidOperationException("Native armor observer contract changed; no compatible patch");
            var puncture=code[passed+1].operand as MethodInfo;
            if(code[passed].opcode!=OpCodes.Ldloc_1||puncture==null||puncture.Name!="NotifyPunctured")
                throw new InvalidOperationException("Native perforation branch changed");
            var decisionMethod=typeof(Mod).GetMethod("ObserveDecision",BindingFlags.Static|BindingFlags.NonPublic);
            var outcomeMethod=typeof(Mod).GetMethod("ObserveOutcome",BindingFlags.Static|BindingFlags.NonPublic);
            for(int i=0;i<code.Count;i++)
            {
                var extra=new List<CodeInstruction>();
                if(i==decision)
                {
                    extra.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    extra.Add(new CodeInstruction(OpCodes.Ldloc_S,code[decision].operand));
                    extra.Add(new CodeInstruction(OpCodes.Ldloc_S,code[decision+1].operand));
                    extra.Add(new CodeInstruction(OpCodes.Call,decisionMethod));
                }
                if(i==passed||i==failed)
                {
                    extra.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    extra.Add(new CodeInstruction(OpCodes.Ldc_I4,i==passed?1:2));
                    extra.Add(new CodeInstruction(OpCodes.Call,outcomeMethod));
                }
                var m=code[i].operand as MethodInfo;
                if(m!=null&&m.DeclaringType==typeof(LiveRound)&&m.Name=="doRicochet")
                {
                    // Original instance+arguments already on the stack; this adds
                    // and consumes its own arguments and leaves that stack intact.
                    extra.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    extra.Add(new CodeInstruction(OpCodes.Ldc_I4_3));
                    extra.Add(new CodeInstruction(OpCodes.Call,outcomeMethod));
                }
                if(extra.Count>0)
                {
                    extra[0].labels.AddRange(code[i].labels);code[i].labels.Clear();
                    foreach(var c in extra)yield return c;
                }
                yield return code[i];
            }
        }
    }
}

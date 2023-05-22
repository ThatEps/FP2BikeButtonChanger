namespace BikeButtonChanger
{
    using BepInEx;
    using HarmonyLib;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;

    /// <summary>
    /// Bike button changer plugin class.
    /// </summary>
    [BepInPlugin("com.eps.plugin.fp2.bike-button-changer", "BikeButtonChanger", "1.0.0")]
    [BepInProcess("FP2.exe")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            var harmony = new Harmony("com.eps.plugin.fp2.bike-button-changer");
            harmony.PatchAll(typeof(PatchFPPlayer_Action_Carol_GroundMoves));
            harmony.PatchAll(typeof(PatchFPPlayer_Action_Carol_AirMoves));
        }
    }

    /// <summary>
    /// FPPlayer.Action_Carol_GroundMoves transpiler class.
    /// </summary>
    [HarmonyPatch(typeof(FPPlayer))]
    [HarmonyPatch(nameof(FPPlayer.Action_Carol_GroundMoves))]
    public static class PatchFPPlayer_Action_Carol_GroundMoves
    {
        /// <summary>
        /// Alters the conditional statement 'if (this.input.guardHold &amp;&amp; this.Action_Carol_JumpDiscWarp(this.carolJumpDisc))' so that it only
        /// triggers if both the jump and special button are being held.
        /// </summary>
        /// <param name="instructions">The original code instructions.</param>
        /// <returns>The modified code instrucitons.</returns>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool foundCanWarp = false;      // Whether we've found the first 'this.canWarp = true;' statement yet.
            bool foundRet = false;          // Whether we've found the first 'return;' statement after the CanWarp statement.
            object brfalseOperand = null;   // Stores the first 'break on false' statement's operand.
            int brfalseIndex = -1;          // Stores the first 'break on false' statement's index.

            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                // Defines the block of instructions within which we'll be doing changes.
                if (foundCanWarp && !foundRet)
                {
                    // After we've found the first 'brfalse' intruction, no further existing instructions are considered.
                    if (brfalseIndex < 0)
                    {
                        // When we encounter a 'ldfld FPPlayerInput::guardHold' instruction, we replace the operand with 'FPPlayerInput::jumpHold'.
                        if ((codes[i].opcode == OpCodes.Ldfld) && (codes[i].operand as FieldInfo != null) && ((FieldInfo)codes[i].operand).Name == "guardHold")
                            codes[i].operand = typeof(FPPlayerInput).GetField("jumpHold");

                        // When we find the first 'brfalse' after CanWarp, we memorize its operand and its index.
                        if (codes[i].opcode == OpCodes.Brfalse)
                        {
                            brfalseOperand = codes[i].operand;
                            brfalseIndex = i;
                        }                    
                    }

                    // Stop at the first 'ret' instruction after CanWarp.
                    if (codes[i].opcode == OpCodes.Ret)
                        foundRet = true;
                }

                // Nothing happens until we find the first 'stfld FPPlayer::canWarp' instruciton. It acts as the anchor for the modification.
                if ((codes[i].opcode == OpCodes.Stfld) && (codes[i].operand as FieldInfo != null) && ((FieldInfo)codes[i].operand).Name == "canWarp")
                    foundCanWarp = true;
            }

            // Inserts '&& this.input.specialhold' into the if condition.
            if (brfalseIndex > -1)
                codes.InsertRange(brfalseIndex + 1, new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),                                                   // this
                    new CodeInstruction(OpCodes.Ldflda, typeof(FPPlayer).GetField("input")),                // this.input
                    new CodeInstruction(OpCodes.Ldfld, typeof(FPPlayerInput).GetField("specialHold")),      // this.input.specialHold
                    new CodeInstruction(OpCodes.Brfalse, brfalseOperand)                                    // Jump to right after the 'return' instruction if false
                });
            
            return codes.AsEnumerable();
        }
    }

    /// <summary>
    /// FPPlayer.Action_Carol_AirMoves has the exact same if-block copy-pasted into it, which produces the exact same MSIL code. So we can re-use the same transpiler.
    /// </summary>
    [HarmonyPatch(typeof(FPPlayer))]
    [HarmonyPatch(nameof(FPPlayer.Action_Carol_AirMoves))]
    public static class PatchFPPlayer_Action_Carol_AirMoves
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return PatchFPPlayer_Action_Carol_GroundMoves.Transpiler(instructions);
        }
    }



/*
    [DETAILED EXPLANATION]

    The code we're looking for in both methods looks like this:

        this.canWarp = true;
        if (this.input.guardHold && this.Action_Carol_JumpDiscWarp(this.carolJumpDisc))
        {
            ...
            return;
        }

    The plugin needs to change guardHold to jumpHold, and also insert an extra check for this.input.specialhold. The MSIL code produced is as follows:

    stfld   bool FPPlayer::canWarp
    ldarg.0
    ldflda  valuetype FPPlayerInput FPPlayer::input
    ldfld   bool FPPlayerInput::guardHold
    brfalse IL_008E
    ldarg.0
    ldarg.0
    ldfld   class CarolJumpDisc FPPlayer::carolJumpDisc
    call    instance bool FPPlayer::Action_Carol_JumpDiscWarp(class CarolJumpDisc)
    brfalse IL_008E
    ...
	ret
    IL_008E: ldarg.0

    The instruction 'stfld bool FPPlayer::canWarp' appears in each method exactly twice - once for summoning the bike, and a second time for a normal dash warp.
    The two always appear in the same order - bike check first, then normal warp further down in the code. As such, we can anchor the tranpiler to the first time
    we encounter 'stfld bool FPPlayer::canWarp' and limit our changes between it and the first 'ret' statement that comes after. Within this block, we want to change
    any 'ldfld' with an operand of 'guardHold' to have an operand of 'jumpHold' instead. We also want to add the extra condition that the Special button must be held
    as well. To do this, we need to replicate the 4 instructions that come after the 'canWarp' instruction. In particular, we need to copy the operand to 'brfalse'
    to be the exact same. This results in the following new instructions being inserted:

    ldarg.0
    ldflda  valuetype FPPlayerInput FPPlayer::input
    ldfld   bool FPPlayerInput::specialHold
    brfalse IL_008E

*/
}

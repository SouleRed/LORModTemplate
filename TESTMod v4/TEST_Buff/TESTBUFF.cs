using HarmonyLib;
using UnityEngine;

namespace TESTMod.TEST_Buff
{
    public class TESTBUFF_01 : BattleUnitBuf
    {
        protected override string keywordId => "TESTBUFF";

        public TESTBUFF_01(BattleUnitModel model, int initialStack)
        {
            _owner = model;
            stack = initialStack;

            if (TESTMod_ModInitializer.ArtWorks.TryGetValue("TESTBUFF", out Sprite icon))
            {
                typeof(BattleUnitBuf).GetField("_bufIcon", AccessTools.all)?.SetValue(this, icon);
                typeof(BattleUnitBuf).GetField("_iconInit", AccessTools.all)?.SetValue(this, true);
            }
        }

        public override void OnRoundEnd()
        {
            if (stack <= 0) RemoveBuf(_owner);
        }

        // ===== 静态管理 =====

        static TESTBUFF_01 Find(BattleUnitModel unit)
        {
            var list = unit?.bufListDetail?.GetActivatedBufList();
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is TESTBUFF_01 buf && !buf.IsDestroyed()) return buf;
            }
            return null;
        }

        public static void GainBuf(BattleUnitModel unit, int value, bool NoDouble = false)
        {
            if (unit == null || value == 0) return;
            var buf = Find(unit);
            if (buf == null) unit.bufListDetail.AddBuf(new TESTBUFF_01(unit, value));
            else buf.stack += value;
        }

        public static void SetBuf(BattleUnitModel unit, int value)
        {
            if (unit == null) return;
            value = Mathf.Max(0, value);
            var buf = Find(unit);
            if (buf == null)
            {
                if (value > 0) unit.bufListDetail.AddBuf(new TESTBUFF_01(unit, value));
            }
            else
            {
                if (value > 0) buf.stack = value;
                else RemoveBuf(unit);
            }
        }

        public static int GetBuf(BattleUnitModel unit) => Find(unit)?.stack ?? 0;

        public static void RemoveBuf(BattleUnitModel unit)
        {
            var buf = Find(unit);
            if (buf == null) return;
            buf._owner?.bufListDetail?.RemoveBuf(buf);
        }
    }
}
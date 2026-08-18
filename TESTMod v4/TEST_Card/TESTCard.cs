namespace TESTMod.TEST_Card
{

    public class DiceCardSelfAbility_TEST_Draw1Next : DiceCardSelfAbilityBase
    {

        public override string[] Keywords => new string[] { "DrawCard_Keyword" };

        public override void OnUseCard()
        {
            owner.bufListDetail.AddBuf(new BattleUnitBuf_TEST_Draw1Next());
        }
    }

    public class BattleUnitBuf_TEST_Draw1Next : BattleUnitBuf
    {
        public override void OnRoundEndTheLast()
        {
            _owner.allyCardDetail.DrawCards(1);
            Destroy();
        }
    }
}

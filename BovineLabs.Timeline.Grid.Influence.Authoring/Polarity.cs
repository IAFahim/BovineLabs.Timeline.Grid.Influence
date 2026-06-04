namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    public enum Polarity : byte
    {
        Additive,
        Subtractive
    }

    public static class PolarityExtensions
    {
        public static int Sign(this Polarity polarity)
        {
            return polarity == Polarity.Subtractive ? -1 : 1;
        }
    }
}
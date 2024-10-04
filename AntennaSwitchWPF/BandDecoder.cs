namespace AntennaSwitchWPF;

public class BandDecoder
{
    public int BandNumber { get; private set; }

    public void DecodeBand(string? frequency)
    {
        if (!long.TryParse(frequency, out var freq))
        {
            BandNumber = 0;
            return;
        }

        BandNumber = freq switch
        {
            >= 1_810_000 and <= 2_000_000 => 1,
            >= 3_500_000 and <= 3_800_000 => 2,
            >= 7_000_000 and <= 7_200_000 => 3,
            >= 10_100_000 and <= 10_150_000 => 4,
            >= 14_000_000 and <= 14_350_000 => 5,
            >= 18_068_000 and <= 18_168_000 => 6,
            >= 21_000_000 and <= 21_450_000 => 7,
            >= 24_890_000 and <= 24_990_000 => 8,
            >= 28_000_000 and <= 29_700_000 => 9,
            >= 50_000_000 and <= 54_000_000 => 10,
            _ => 0
        };
    }
}
namespace AntennaSwitchWPF;

public class BandDecoder
{
    public int BandNumber { get; set; }

    public void DecodeBand(string? frequency)
    {
        if (frequency == null) return;
        var freq = int.Parse(frequency);

        if (string.IsNullOrEmpty(frequency)) return;

        BandNumber = freq switch
        {
            >= 1810000 and <= 2000000 => 0,
            >= 3500000 and <= 3800000 => 1,
            >= 7000000 and <= 7200000 => 2,
            >= 10100000 and <= 10150000 => 3,
            >= 14000000 and <= 14350000 => 4,
            >= 18068000 and <= 18168000 => 5,
            >= 21000000 and <= 21450000 => 6,
            >= 24890000 and <= 24990000 => 7,
            >= 28000000 and <= 29700000 => 8,
            >= 50000000 and <= 54000000 => 9,
            _ => 0
        };
    }
}
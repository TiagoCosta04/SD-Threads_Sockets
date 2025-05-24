namespace Shared.Models
{
    public class AggregatedData
    {
        public string AgregadorId { get; set; } = "";
        public List<WavyMessage> Messages { get; set; } = new();
        public string Timestamp { get; set; } = "";
    }
}

using System.Text.Json.Serialization;

namespace Shared.Models
{
    public class WavyMessage
    {
        [JsonPropertyName("wavy_id")]
        public string WavyId { get; set; } = "";
        
        [JsonPropertyName("sensors")]
        public SensorData[] Sensors { get; set; } = Array.Empty<SensorData>();
        
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = "";
        
        [JsonPropertyName("agregador_id")]
        public string AgregadorId { get; set; } = "";
    }

    public class SensorData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";
        
        [JsonPropertyName("value")]
        public double Value { get; set; }
    }
}

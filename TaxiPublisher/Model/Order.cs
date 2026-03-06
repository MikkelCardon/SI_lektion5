

namespace TaxiPublisher.Model
{
    public class Order
    {
        public string? Id { get; set; }
        public string Destination { get; set; } = string.Empty;
        public bool QuickOrder { get; set; }
        public DateTime? PickUpTime { get; set; }
        
        public CarSize? Size{ get; set; }
        public Boolean IsElectric { get; set; }
    }
    
    public enum CarSize
    {
        Small,
        Medium,
        Large
    }
}

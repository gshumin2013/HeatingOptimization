namespace Heating.Models
{
    public class Fuel
    {
        public string Name { get; set; }
        public double Amount { get; set; }
        public List<double> Heating_values { get; set; } = new List<double>();
    }
    public class Heating_Unit
    {
        public string Name { get; set; }
        public int MinLoad { get; set; }
        public List<double> Loads { get; set; } = new List<double>();
    }
    public class Result 
    {
        public string Name { get; set; } 
        public double Amount { get; set; }
        public double HeatProduced { get; set; } 
    }
}

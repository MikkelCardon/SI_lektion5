using Microsoft.EntityFrameworkCore;
using TaxiPublisher.Model;

namespace TaxiPublisher.Db
{
    public class OrdersContext : DbContext
    {
        public OrdersContext(DbContextOptions options) : base(options)
        {
            
        }
        public DbSet<Order> Orders { get; set; }
        
        
    }
}

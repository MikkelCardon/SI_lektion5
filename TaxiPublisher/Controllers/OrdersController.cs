using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using System.Text.Json;
using System.Threading.Channels;
using TaxiPublisher.Db;
using TaxiPublisher.Model;
using TaxiPublisher.Services;

namespace TaxiPublisher.Controllers
{
    
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private static int _orderId = 0;
        private readonly IChannel _channel;
        private readonly OrdersContext _ordersContext;

        public OrdersController(IChannel channel, OrdersContext ordersContext)
        {
            _channel = channel;
            _ordersContext = ordersContext;
        }

        // POST api/<OrderController>
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] Order order)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem();
            }
            
            order.Id = _orderId++.ToString();
            _ordersContext.Orders.Add(order);
            _ordersContext.SaveChanges();
            var message = JsonSerializer.Serialize(order);
            var body = System.Text.Encoding.UTF8.GetBytes(message);
            await _channel.BasicPublishAsync(exchange: "orders",
                                     routingKey: string.Empty,
                                     body: body);
            return Ok();
        }

    }
}

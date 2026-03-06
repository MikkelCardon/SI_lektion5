using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using TaxiSubscriber.Model;
using System.Text.Json;

namespace TaxiSubscriber
{
    internal class Program
    {
        private static readonly List<Order> _orders = [];
        static async Task Main(string[] args)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();
            
            Guid consumerId = Guid.NewGuid();

            await channel.ExchangeDeclareAsync(exchange: "orders", type: ExchangeType.Topic);
            QueueDeclareOk queueDeclareResult = await channel.QueueDeclareAsync(queue: $"ordersExchangeQueue-{consumerId}");
            string queueName = queueDeclareResult.QueueName;

            string routingKey = GetRoutingKey();

            Console.WriteLine($"Subscribed to : {routingKey}");
            
            await channel.QueueBindAsync(queue: queueName, exchange: "orders", routingKey: routingKey);
            Console.WriteLine("Venter på order.");

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (model, ea) =>
            {
                byte[] body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                Order? order = JsonSerializer.Deserialize<Order>(message);
                Console.WriteLine($"Ny order modtaget");
                if (order is not null)
                {
                    _orders.Add(order);
                    PrintOrders();
                }

                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(queueName, autoAck: true, consumer: consumer);
            
            await channel.ExchangeDeclareAsync(exchange: "reply-exchange", type: ExchangeType.Fanout);
            var replyQueue = await channel.QueueDeclareAsync(queue: $"replyExchangeQueue-{consumerId}");
            await channel.QueueBindAsync(queue: $"replyExchangeQueue-{consumerId}", exchange: "reply-exchange", routingKey: string.Empty);
            
            Thread thread = new Thread(async () =>
            {
                await Reply(channel, consumerId);
            });
            thread.Start();
            
            while (true)
            {
                string? input = Console.ReadLine();
                await RequestReply(channel, input, replyQueue, consumerId);
            }

        }

        private static async Task RequestReply(IChannel channel, string input, QueueDeclareOk replyQueue, Guid consumerId)
        {
            await Request(channel, input, replyQueue, consumerId);
        }

        private static async Task Reply(IChannel channel, Guid consumerId)
        {
            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                Console.WriteLine($"Reply Recieved: {message}");
                
                RemoveOrder(message, consumerId);

                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(queue: $"replyExchangeQueue-{consumerId}", autoAck: true, consumer: consumer);
            Console.WriteLine("Listening on 'reply-queue'...");
            //await channel.BasicConsumeAsync(queue: replyQueue.QueueName, autoAck: true, consumer: consumer);
        }

        private static async Task Request(IChannel channel, string input, QueueDeclareOk replyQueue, Guid consumerId)
        {
            await channel.QueueDeclareAsync("request-queue", exclusive: false);
            
            var properties = new BasicProperties()
            {
                ReplyTo = replyQueue.QueueName,
                CorrelationId = consumerId.ToString(),
            };
            
            var message = input;
            var body = Encoding.UTF8.GetBytes(message);
            
            Console.WriteLine($"Sending Request: {properties.CorrelationId} - id: {message}");
            await channel.BasicPublishAsync(string.Empty, "request-queue", true, properties, body);
        }

        private static void PrintOrders()
        {
            Console.WriteLine("Id  | Destination | Afhentnings tidspunkt");
            Console.WriteLine("-----------------------------------------");
            _orders.ForEach(order =>
            {
                string? pickUpTime = order.QuickOrder ? "Snarest muligt" : order.PickUpTime.ToString();
                Console.WriteLine($"{order.Id} | {order.Destination} | {pickUpTime}");
            });
            Console.WriteLine("-----------------------------------------");
            Console.WriteLine("Vælg en order ved at indtaste id'et");
        }

        private static void RemoveOrder(string json, Guid consumerId)
        {
            var result = JsonSerializer.Deserialize<JsonElement>(json);

            string resultConsumerId = result.GetProperty("ConsumerId").GetString();
            string resultOrderId = result.GetProperty("OrderId").GetString();

            if (resultConsumerId == consumerId.ToString())
            {
                Console.WriteLine($"Order accepted: {resultOrderId}");
            }

            if (resultOrderId is not null)
            {
                Console.WriteLine($"Forsøger at slette id: {resultOrderId}");
                _orders.RemoveAll(order => order.Id == resultOrderId);
            }
            PrintOrders();
        }
        
        public static string GetRoutingKey()
        {
            string routingKey = "taxi.";
            Console.WriteLine("size (Small, Medium, Large");
            string size = Console.ReadLine();
            
            Console.WriteLine("electric (*/electric");
            string electric = Console.ReadLine();
            
            if (size.Equals("*") && electric.Equals("*"))
            {
                routingKey+= "#";
                return routingKey;
            }

            routingKey += size;
            routingKey += "." + electric;

            return routingKey;
        }
    }
}

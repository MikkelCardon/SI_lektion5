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

            await channel.ExchangeDeclareAsync(exchange: "orders", type: ExchangeType.Fanout);
            QueueDeclareOk queueDeclareResult = await channel.QueueDeclareAsync();
            string queueName = queueDeclareResult.QueueName;
            await channel.QueueBindAsync(queue: queueName, exchange: "orders", routingKey: string.Empty);
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
            
            var replyQueue = await channel.QueueDeclareAsync("reply-queue", exclusive:false);
            Guid consumerId = Guid.NewGuid();
            
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

            await channel.BasicConsumeAsync(queue: "reply-queue", autoAck: true, consumer: consumer);
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
    }
}

using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using TaxiSubscriber.Model;

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
            
            
            while (true)
            {
                string? input = Console.ReadLine();
                await RequestReply(channel, input);
                
                Thread.Sleep(100);
            }

        }

        private static async Task RequestReply(IChannel channel, string input)
        {
            var replyQueue = await channel.QueueDeclareAsync();
            
            await Request(channel, input, replyQueue);
            await Reply(channel, input, replyQueue);
        }

        private static async Task Reply(IChannel channel, string input, QueueDeclareOk replyQueue)
        {
            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                Console.WriteLine($"Reply Recieved: {message}");
                
                RemoveOrder(message);

                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(queue: replyQueue.QueueName, autoAck: true, consumer: consumer);
        }

        private static async Task Request(IChannel channel, string input, QueueDeclareOk replyQueue)
        {
            await channel.QueueDeclareAsync("request-queue", exclusive: false);
            
            var properties = new BasicProperties()
            {
                ReplyTo = replyQueue.QueueName,
                CorrelationId = Guid.NewGuid().ToString()
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

        private static void RemoveOrder(string input)
        {
            if (input is not null)
            {
                Console.WriteLine($"Forsøger at slette id: {input}");
                _orders.RemoveAll(order => order.Id == input);
                PrintOrders();
            }
        }
    }
}

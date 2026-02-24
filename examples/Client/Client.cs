using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace Client
{
    internal class Client
    {
        static async Task Main(string[] args)
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };

            using var connection = await factory.CreateConnectionAsync();

            using var channel = await connection.CreateChannelAsync();

            var replyQueue = await channel.QueueDeclareAsync();
            await channel.QueueDeclareAsync("request-queue", exclusive: false);

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                Console.WriteLine($"Reply Recieved: {message}");
                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(queue: replyQueue.QueueName, autoAck: true, consumer: consumer);
            //Opsætter queue, event ved consumed og starter comsumeren.
            
            
            var properties = new BasicProperties()
            {
                ReplyTo = replyQueue.QueueName,
                CorrelationId = Guid.NewGuid().ToString()
            };

            var message = "Can I request a reply?";
            var body = Encoding.UTF8.GetBytes(message);

            Console.WriteLine($"Sending Request: {properties.CorrelationId}");

            //Laver et svar ud til default exchange og routingKey 'request-queue', og sender properties og body med.
            await channel.BasicPublishAsync(string.Empty, "request-queue", true, properties, body);
            Console.WriteLine(" Press [enter] to exit.");
            Console.ReadKey();

        }

    }
}


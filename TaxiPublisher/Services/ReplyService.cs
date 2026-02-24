using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TaxiPublisher.Db;
using TaxiPublisher.Model;

namespace TaxiPublisher.Services;

public class ReplyService : BackgroundService
{
    private IChannel _channel;
    private readonly string _queueName;
    
    private readonly OrdersContext _ordersContext;

    public ReplyService(IChannel ichannel, OrdersContext ordersContext)
    {
        _channel = ichannel;
        _queueName = "request-queue";
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _channel.QueueDeclareAsync("request-queue", exclusive: false);
        
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (model, ea) =>
        {
            Console.WriteLine($"Received Request: {ea.BasicProperties.CorrelationId}");

            var replyMessage = $"This is your reply: {ea.BasicProperties.CorrelationId}";
            var replyProperties = new BasicProperties
            {
                CorrelationId = ea.BasicProperties.CorrelationId
            };
            var eabody = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(eabody);
            await DeleteOrderAsync(message);
            
            var body = Encoding.UTF8.GetBytes(replyMessage);

            if (string.IsNullOrEmpty(ea.BasicProperties.ReplyTo))
            {
                Console.WriteLine("No reply-to property set. Cannot send reply.");
                return;
            }

            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: ea.BasicProperties.ReplyTo,
                mandatory: true,
                basicProperties: replyProperties,
                body: body
            );
        };

        await _channel.BasicConsumeAsync(queue: "request-queue", autoAck: true, consumer: consumer);
    }
    
    public async Task<bool> DeleteOrderAsync(string orderIdString)
    {
        int orderId = Int32.Parse(orderIdString);
        // 1. Find the order by ID
        var order = await _ordersContext.Orders.FindAsync(orderId);

        // 2. If found, remove it
        if (order != null)
        {
            _ordersContext.Orders.Remove(order);
            await _ordersContext.SaveChangesAsync();
            return true;
        }

        // 3. Return false if not found
        return false;
    }
}
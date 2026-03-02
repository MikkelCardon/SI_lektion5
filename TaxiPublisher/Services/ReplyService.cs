using System.Text;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TaxiPublisher.Db;
using TaxiPublisher.Model;
using System.Text.Json;

namespace TaxiPublisher.Services;

public class ReplyService : BackgroundService
{
    private IChannel _channel;
    private readonly string _queueName;

    private readonly IServiceScopeFactory _scopeFactory;

    public ReplyService(IChannel ichannel, IServiceScopeFactory scopeFactory)
    {
        _channel = ichannel;
        _queueName = "request-queue";
        _scopeFactory = scopeFactory;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _channel.QueueDeclareAsync("request-queue", exclusive: false);
        
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (model, ea) =>
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrdersContext>();
            
            Console.WriteLine($"Received Request: {ea.BasicProperties.CorrelationId}");

            var replyMessage = $"This is your reply: {ea.BasicProperties.CorrelationId}";
            var replyProperties = new BasicProperties
            {
                CorrelationId = ea.BasicProperties.CorrelationId
            };
            var eabody = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(eabody);
            
            var deleted = await DeleteOrderAsync(context, message);

            var jsonBody = "";
            if (deleted)
            {
                var body = new {OrderId = message, ConsumerId = ea.BasicProperties.CorrelationId};
                jsonBody = JsonSerializer.Serialize(body);
            }
            else
            {
                var body = new {OrderId = "", ConsumerId = ea.BasicProperties.CorrelationId};
                jsonBody = JsonSerializer.Serialize(body);
            }
            var bytearray = Encoding.UTF8.GetBytes(jsonBody);

            if (string.IsNullOrEmpty(ea.BasicProperties.ReplyTo))
            {
                Console.WriteLine("No reply-to property set. Cannot send reply.");
                return;
            }
            
            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: "reply-queue",
                mandatory: true,
                basicProperties: replyProperties,
                body: bytearray
            );
            Console.WriteLine($"Replied to: {ea.BasicProperties.ReplyTo}");
            
        };

        await _channel.BasicConsumeAsync(queue: "request-queue", autoAck: true, consumer: consumer);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
    
    public async Task<bool> DeleteOrderAsync(OrdersContext ordersContext, string orderId)
    {
        Console.WriteLine("Updating db...");
        try
        {
            // 1. Find the order by ID
            var order = await ordersContext.Orders.FindAsync(orderId);

            // 2. If found, remove it
            if (order != null)
            {
                ordersContext.Orders.Remove(order);
                await ordersContext.SaveChangesAsync();

                Console.WriteLine(
                    $"Deleted order {orderId}. Remaining orders: {await ordersContext.Orders.CountAsync()}");

                return true;
            }

            // 3. Return false if not found
            Console.WriteLine("Nothing found");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return false;
        }
    }
}
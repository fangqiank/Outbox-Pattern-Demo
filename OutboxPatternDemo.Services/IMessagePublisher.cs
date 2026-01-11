namespace OutboxPatternDemo.Services
{
    public interface IMessagePublisher
    {
        Task<bool> PublishAsync(
            string messageType, 
            string content, 
            CancellationToken cancellationToken);
    }
}
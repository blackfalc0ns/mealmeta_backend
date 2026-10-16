namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

/// <summary>Publishes a committed delivery change to the shared realtime invalidation channel.</summary>
public interface ICustomerDeliveryChangeTransport
{
    Task PublishAsync(CustomerDeliveryChangedV1 change, CancellationToken cancellationToken = default);
}

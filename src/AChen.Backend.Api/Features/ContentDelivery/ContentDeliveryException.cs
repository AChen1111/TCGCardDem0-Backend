using AChen.Backend.Api.Infrastructure;

namespace AChen.Backend.Api.Features.ContentDelivery;

public class ContentDeliveryException(int statusCode, string code, string message)
    : ApiException(statusCode, code, message);

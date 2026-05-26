using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OdataQueryLite.Parsing;

namespace OdataQueryLite.AspNetCore
{
    // Catches OdataQueryException at the pipeline boundary and converts to HTTP 400.
    // Without this, model-binder parse failures bubble as 500 (framework treats them as
    // unhandled), masking what was actually a client mistake. UnsupportedQueryOptionException
    // (a subclass) is mapped the same way — both are client-input errors per the
    // OdataQueryException contract.
    public sealed class UnsupportedQueryOptionMiddleware(
        RequestDelegate next,
        ILogger<UnsupportedQueryOptionMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            try
            {
                await next(context);
            }
            catch (OdataQueryException ex)
            {
                if (context.Response.HasStarted)
                {
                    // Cannot rewrite a response that's already on the wire — let it surface
                    // so the user sees the framework's default behavior rather than a
                    // half-streamed JSON payload.
                    logger.LogWarning(ex, "OdataQueryException raised after response started; cannot convert to 400.");
                    throw;
                }
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json; charset=utf-8";

                var payload = new ErrorPayload(
                    Error: "BadRequest",
                    Message: ex.Message,
                    Option: ex is UnsupportedQueryOptionException uq ? uq.OptionName : null);
                await JsonSerializer.SerializeAsync(context.Response.Body, payload, ErrorPayloadJsonContext.Default.ErrorPayload, context.RequestAborted);
            }
        }

        internal sealed record ErrorPayload(string Error, string Message, string? Option);
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(UnsupportedQueryOptionMiddleware.ErrorPayload))]
    internal sealed partial class ErrorPayloadJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}

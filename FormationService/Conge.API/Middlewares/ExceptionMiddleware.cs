using Conge.Domain.Exceptions;
using FluentValidation;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using ValidationException = FluentValidation.ValidationException;
namespace Conge.API.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
                return;

            _logger.LogError(ex, "Une erreur est survenue: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            ValidationException ve => (HttpStatusCode.BadRequest,
                string.Join(", ", ve.Errors.Select(e => e.ErrorMessage))),

            SoldeInsuffisantException => (HttpStatusCode.UnprocessableEntity, exception.Message),
            EligibiliteException => (HttpStatusCode.UnprocessableEntity, exception.Message),
            CongeNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            EmployeNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            SoldeNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Forbidden, exception.Message),
            InvalidOperationException => (HttpStatusCode.Conflict, exception.Message),
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message),

            _ => (HttpStatusCode.InternalServerError, "Une erreur interne est survenue.")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            status = (int)statusCode,
            message,
            timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
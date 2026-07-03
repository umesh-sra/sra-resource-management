using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SraRms.Api.Filters;

/// <summary>
/// Converts Postgres unique-constraint violations (SQLSTATE 23505) surfaced as
/// <see cref="DbUpdateException"/> into the same 409 problem+json the controllers'
/// explicit duplicate checks return. The controllers check-then-insert, so a
/// concurrent duplicate can slip past the check and hit the database's unique
/// index; without this filter that surfaces as an unhandled 500.
/// </summary>
public sealed class UniqueViolationExceptionFilter(ProblemDetailsFactory problemDetailsFactory) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateException
            {
                InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            })
        {
            return;
        }

        var problem = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            detail: $"A record with the same unique value already exists (constraint: {pg.ConstraintName ?? "unknown"}).");

        context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        context.ExceptionHandled = true;
    }
}

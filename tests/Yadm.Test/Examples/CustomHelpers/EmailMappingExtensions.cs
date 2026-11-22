using System.Runtime.CompilerServices;

namespace Yadm.Test;

/// <summary>
/// A helper written outside the library, using only the public contract. Forwarding <c>name</c> and
/// <c>expression</c> gives it the same path rules as the built-in helpers.
/// </summary>
public static class EmailMappingExtensions
{
    extension(MappingContext context)
    {
        public string? Email(string? text, string? name = null,
            [CallerArgumentExpression(nameof(text))] string expression = "")
        {
            if (!context.TryRequired(text, out var email, name, expression) || email.Contains('@'))
                return email;

            context.Reject(email, "Email", $"'{email}' is not an email address.", name, expression);
            return null;
        }
    }
}

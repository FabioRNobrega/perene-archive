using System.Net;
using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class ArchiveMutationMessagesTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ArchiveMutationMessages.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized, ArchiveMutationMessages.SessionExpired)]
    [InlineData(HttpStatusCode.NotFound, ArchiveMutationMessages.NotAvailable)]
    [InlineData(HttpStatusCode.Conflict, ArchiveMutationMessages.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, ArchiveMutationMessages.Fallback)]
    [InlineData(HttpStatusCode.InternalServerError, ArchiveMutationMessages.Fallback)]
    [InlineData(HttpStatusCode.TooManyRequests, ArchiveMutationMessages.Fallback)]
    public void Each_status_maps_to_its_own_message(HttpStatusCode status, string expected)
    {
        Assert.Equal(expected, ArchiveMutationMessages.For(status));
    }

    [Fact]
    public void A_permission_failure_names_permissions_and_not_a_generic_save_error()
    {
        var message = ArchiveMutationMessages.For(HttpStatusCode.Forbidden);

        Assert.Contains("permission", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("could not be saved", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_or_hidden_item_does_not_reveal_which_one_it_was()
    {
        var message = ArchiveMutationMessages.For(HttpStatusCode.NotFound);

        Assert.Contains("no longer available", message);
        Assert.Contains("don't have access", message);
    }

    [Fact]
    public void Every_message_is_distinct_and_non_empty()
    {
        var messages = new[]
        {
            ArchiveMutationMessages.Forbidden, ArchiveMutationMessages.SessionExpired, ArchiveMutationMessages.NotAvailable,
            ArchiveMutationMessages.Conflict, ArchiveMutationMessages.Fallback
        };

        Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
        Assert.Equal(messages.Length, messages.Distinct().Count());
    }
}

namespace CitizenServicesCopilot.Application.Common.Exceptions;

public sealed class InvalidEditedContentException : Exception
{
    public InvalidEditedContentException(string message)
        : base($"edited draft content is invalid: {message}")
    {
    }

    public InvalidEditedContentException(string message, Exception inner)
        : base($"edited draft content is invalid: {message}", inner)
    {
    }
}
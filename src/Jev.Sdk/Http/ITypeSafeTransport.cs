// ITypeSafeTransport.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the transport seam.
// See requirements/requirements.md, R12 and R13.
//
// Type: ITypeSafeTransport

namespace Jev.Sdk;

/// <summary>
/// Sends an HTTP request and returns the response. This is the client's one external
/// dependency, and the seam a test substitutes.
/// </summary>
/// <remarks>
/// The interface carries no authentication logic, no retry logic, and no serialization.
/// Everything except "perform this exchange" lives above it, which keeps a substitute trivial
/// to write and keeps retries testable without a network.
/// </remarks>
public interface ITypeSafeTransport
{
    /// <summary>
    /// Sends a request and reads the response body to completion.
    /// </summary>
    /// <param name="request">The exchange to perform.</param>
    /// <param name="cancellationToken">Cancels the exchange, including the body read.</param>
    /// <returns>The response, with its body materialised.</returns>
    /// <exception cref="JevConnectionException">The exchange could not be completed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken);
}

using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Payments;
using DataVerification.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Tickets.Queries;

/// <summary>
/// A file attached to a ticket, read by support.
/// </summary>
/// <remarks>
/// Admin-only, and deliberately so. The sender is identified by an email address they typed, which
/// is not proof of anything — serving their attachments back to whoever quotes the ticket number
/// would turn a public form into a file host anyone could read from.
/// </remarks>
public sealed record GetTicketFileQuery(Guid TicketId, Guid FileId) : IRequest<FileDownload>;

public sealed class TicketDownloadHandlers : IRequestHandler<GetTicketFileQuery, FileDownload>
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;

    public TicketDownloadHandlers(IApplicationDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<FileDownload> Handle(
        GetTicketFileQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Matched on the ticket as well as the file: without it, any file id would resolve through
        // any ticket the caller happens to be looking at.
        var file = await _db.TicketFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.FileId && candidate.TicketId == request.TicketId,
                cancellationToken)
            ?? throw new NotFoundException(nameof(TicketFile), request.FileId);

        var content = await _storage.OpenReadAsync(file.StoragePath, cancellationToken);

        return new FileDownload(content, file.ContentType, file.FileName);
    }
}

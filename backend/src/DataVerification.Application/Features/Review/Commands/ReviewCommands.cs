using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Applications;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Review.Commands;

// ------------------------------------------------------------ Status changes

/// <summary>
/// Moves an application through the review lifecycle. Admin only. A single change can carry two
/// comments recorded on the thread alongside the transition: <paramref name="UserComment"/> is
/// visible to the applicant (and included in the notification email), while
/// <paramref name="InternalComment"/> is an admins-only note. Both are optional.
///
/// <paramref name="Documents"/> carries any files the reviewer is attaching with the decision,
/// each with the details they recorded beside it. Documents and status move together or not at
/// all: an illegal transition stores nothing.
/// </summary>
public sealed record ChangeApplicationStatusCommand(
    Guid ApplicationId,
    ApplicationStatus ToStatus,
    string? UserComment = null,
    string? InternalComment = null,
    IReadOnlyList<AttachedDocumentInput>? Documents = null) : IRequest<ApplicationStatusResultDto>;

public sealed record ApplicationStatusResultDto(
    Guid ApplicationId,
    string ApplicationNumber,
    ApplicationStatus Status,
    string StatusName);

/// <summary>
/// Which target statuses an administrator may set, and what each one costs in trust.
/// </summary>
public static class AdminSettableStatuses
{
    /// <summary>The review workflow proper — available to anyone with Applications.Review.</summary>
    public static readonly IReadOnlySet<ApplicationStatus> Review = new HashSet<ApplicationStatus>
    {
        ApplicationStatus.InProgress,
        ApplicationStatus.MissedInfo,
        ApplicationStatus.Success,
        ApplicationStatus.Failed,
    };

    /// <summary>
    /// Steps that belong to the applicant, performed on their behalf. Gated behind
    /// Applications.OverrideStatus: submitting for them is harmless, but moving an application
    /// into the review queue marks it paid without any money changing hands.
    /// </summary>
    public static readonly IReadOnlySet<ApplicationStatus> Override = new HashSet<ApplicationStatus>
    {
        ApplicationStatus.Draft,
        ApplicationStatus.PendingPayment,
        ApplicationStatus.Pending,
    };

    /// <summary>
    /// Refunded is absent on purpose and always will be. Setting it here would mark the
    /// application refunded while leaving the money where it is; refunds go through
    /// <see cref="Wallets.Commands.RefundApplicationCommand"/>, which credits the wallet.
    /// </summary>
    public static bool IsSettable(ApplicationStatus status) =>
        Review.Contains(status) || Override.Contains(status);
}

public sealed class ChangeApplicationStatusCommandValidator
    : AbstractValidator<ChangeApplicationStatusCommand>
{
    public ChangeApplicationStatusCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.UserComment).MaximumLength(4000);
        RuleFor(c => c.InternalComment).MaximumLength(4000);

        RuleFor(c => c.Documents)
            .Must(documents => documents is null || documents.Count <= AttachedDocumentLimits.MaxDocuments)
            .WithMessage($"Attach at most {AttachedDocumentLimits.MaxDocuments} documents at a time.");

        RuleForEach(c => c.Documents).SetValidator(new AttachedDocumentInputValidator());

        // Structural only. Whether the caller may set an override status is a permission question,
        // answered in the handler where the current user is known.
        RuleFor(c => c.ToStatus)
            .Must(AdminSettableStatuses.IsSettable)
            .WithMessage("Refunded cannot be set directly; use the refund action so the wallet is credited.");
    }
}

public sealed class ChangeApplicationStatusCommandHandler
    : IRequestHandler<ChangeApplicationStatusCommand, ApplicationStatusResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IDateTimeProvider _clock;
    private readonly ApplicationWriteService _writeService;
    private readonly AttachedDocumentWriter _documentWriter;

    public ChangeApplicationStatusCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger,
        IEmailSender emailSender,
        IEmailTemplateRenderer templateRenderer,
        IDateTimeProvider clock,
        ApplicationWriteService writeService,
        AttachedDocumentWriter documentWriter)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _clock = clock;
        _writeService = writeService;
        _documentWriter = documentWriter;
    }

    /// <summary>
    /// Submits a draft on the applicant's behalf under exactly the rules they face: every mandatory
    /// document uploaded, and every document's custom fields filled in. Without this an
    /// administrator could push a half-finished application into the queue, which is precisely the
    /// state the review workflow assumes cannot occur.
    /// </summary>
    private async Task<ApplicationStatusHistory> SubmitForApplicantAsync(
        VerificationApplication application,
        Actor actor,
        CancellationToken cancellationToken)
    {
        var requiredFiles = await _writeService.GetRequiredFileStatusAsync(
            application,
            _currentUser.LanguageCode,
            cancellationToken);

        if (!ApplicationDetailsProjection.AllMandatoryFilesPresent(requiredFiles))
        {
            var missing = requiredFiles
                .Where(f => f.IsMandatory && !f.IsSatisfied)
                .Select(f => f.Name)
                .Distinct();

            throw new ConflictException(
                "application.mandatory_files_missing",
                $"Upload every mandatory file before submitting: {string.Join(", ", missing)}.");
        }

        var incompleteFields = requiredFiles
            .Where(f => !f.AreFieldsComplete)
            .Select(f => f.Name)
            .Distinct()
            .ToList();

        if (incompleteFields.Count > 0)
        {
            throw new ConflictException(
                "application.document_fields_incomplete",
                $"Complete the details for: {string.Join(", ", incompleteFields)}.");
        }

        return application.Submit(actor);
    }

    public async Task<ApplicationStatusResultDto> Handle(
        ChangeApplicationStatusCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The children are loaded because submitting on the applicant's behalf runs the same
        // completeness checks they face, and those count Names, Services and Files — left unloaded
        // they would report a complete application as missing everything.
        var application = await _db.Applications
            .Include(a => a.Order)
            .Include(a => a.Names)
            .Include(a => a.Services)
            .Include(a => a.Files)
            .Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException("Application", request.ApplicationId);

        var from = application.Status;
        var actor = _currentUser.ToActor();

        // Acting for the applicant is a separate grant from reviewing their work.
        var isOverride = AdminSettableStatuses.Override.Contains(request.ToStatus);

        if (isOverride && !_currentUser.Permissions.Contains(Permissions.ApplicationsOverrideStatus))
        {
            throw new ForbiddenAccessException(
                $"Setting '{request.ToStatus}' requires the {Permissions.ApplicationsOverrideStatus} permission.");
        }

        var userComment = string.IsNullOrWhiteSpace(request.UserComment)
            ? null
            : request.UserComment.Trim();
        var internalComment = string.IsNullOrWhiteSpace(request.InternalComment)
            ? null
            : request.InternalComment.Trim();

        // The status-history note is surfaced on the applicant's own timeline, so it must never
        // carry the internal note — only the user-facing comment (or nothing) is safe to record here.
        var historyNote = userComment;

        // Two targets are more than a transition, so they go through the domain methods that own
        // the extra rules rather than a bare TransitionTo. Illegal transitions throw
        // application.illegal_status_transition from the domain in every case.
        // Two targets are more than a transition, so they go through the domain methods that own
        // the extra rules rather than a bare TransitionTo. Illegal transitions throw
        // application.illegal_status_transition from the domain in every case.
        var history = request.ToStatus switch
        {
            // Submitting for the applicant must still refuse an incomplete draft, exactly as their
            // own submit does — an application missing mandatory documents cannot be reviewed.
            ApplicationStatus.PendingPayment when from == ApplicationStatus.Draft
                => await SubmitForApplicantAsync(application, actor, cancellationToken),

            // MarkPaid stamps PaidAtUtc, so the row is not left "in the queue but never paid".
            // No wallet is debited: that is what makes this an override rather than a payment.
            ApplicationStatus.Pending when from == ApplicationStatus.PendingPayment
                => application.MarkPaid(actor, _clock.UtcNow),

            _ => application.TransitionTo(request.ToStatus, actor, historyNote),
        };

        // Submit and MarkPaid take no note of their own; attach it so the applicant's timeline
        // reads the same however the transition was reached.
        history.Note ??= historyNote;

        // Both comments are recorded on the thread as part of the same change. Creating them here
        // rather than through the comment handler is deliberate: the reviewer picked the target
        // status explicitly, so a user-visible comment must not also auto-transition the application.
        if (userComment is not null)
        {
            _db.ApplicationComments.Add(
                ApplicationComment.FromAdmin(application.Id, actor, userComment, CommentVisibility.ForUser));
        }

        if (internalComment is not null)
        {
            _db.ApplicationComments.Add(
                ApplicationComment.FromAdmin(application.Id, actor, internalComment, CommentVisibility.Internal));
        }

        // Staged after the transition on purpose: an illegal status change throws above, so a
        // refused decision never leaves documents behind. Both are committed by the save below.
        var documents = request.Documents is { Count: > 0 }
            ? await _documentWriter.StageAsync(
                application,
                request.Documents,
                application.Status,
                cancellationToken)
            : [];

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.StatusChanged",
            "Application",
            application.Id,
            new
            {
                application.ApplicationNumber,
                From = from,
                To = application.Status,
                HasUserComment = userComment is not null,
                HasInternalComment = internalComment is not null,

                // Named rather than counted: the trail should say what was filed with a decision,
                // and whether any of it was withheld from the applicant.
                Documents = documents.Count == 0
                    ? null
                    : documents.Select(d => new
                    {
                        d.NameEn,
                        Files = d.Files.Count,
                        Fields = d.Fields.Count,
                        d.IsVisibleToApplicant,
                    }).ToList(),

                // Recorded so a later reconciliation can tell an application that was paid for
                // from one an administrator pushed through by hand.
                AdminOverride = isOverride ? true : (bool?)null,
            },
            cancellationToken);

        // The applicant is told about the new status by email, and sees the user-facing comment (if
        // any) in the body. The internal note is never sent. Delivery failures are swallowed by the
        // sender, so a lost email can never roll back a committed status change.
        var recipient = application.Order?.Email;
        if (!string.IsNullOrWhiteSpace(recipient))
        {
            var message = _templateRenderer.RenderApplicationStatusChanged(
                recipient,
                application.ApplicationNumber,
                application.Status,
                userComment,
                application.Order?.LanguageCode ?? "en");

            await _emailSender.SendAsync(message, cancellationToken);
        }

        return new ApplicationStatusResultDto(
            application.Id,
            application.ApplicationNumber,
            application.Status,
            application.Status.ToString());
    }
}

// ----------------------------------------------------------- Admin comments

/// <summary>
/// Posts a review comment. A user-visible comment on an in-progress application also flips it to
/// MissedInfo, because that is exactly what asking the applicant for something means.
/// </summary>
public sealed record AddAdminCommentCommand(
    Guid ApplicationId,
    string Body,
    CommentVisibility Visibility) : IRequest<CommentDto>;

public sealed class AddAdminCommentCommandValidator : AbstractValidator<AddAdminCommentCommand>
{
    public AddAdminCommentCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.Body).NotEmpty().MaximumLength(4000);
        RuleFor(c => c.Visibility).IsInEnum();
    }
}

public sealed class AddAdminCommentCommandHandler : IRequestHandler<AddAdminCommentCommand, CommentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public AddAdminCommentCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<CommentDto> Handle(
        AddAdminCommentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await _db.Applications
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException("Application", request.ApplicationId);

        var actor = _currentUser.ToActor();

        var comment = ApplicationComment.FromAdmin(
            application.Id,
            actor,
            request.Body.Trim(),
            request.Visibility);

        _db.ApplicationComments.Add(comment);

        // Only a user-facing comment changes state, and only from InProgress. An internal note is
        // invisible to the applicant, so it must never move the application.
        var autoTransitionedFrom = (ApplicationStatus?)null;

        if (request.Visibility == CommentVisibility.ForUser
            && application.Status == ApplicationStatus.InProgress)
        {
            autoTransitionedFrom = application.Status;
            application.TransitionTo(
                ApplicationStatus.MissedInfo,
                actor,
                "Additional information requested");
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.CommentAdded",
            "Application",
            application.Id,
            new { request.Visibility, application.Status },
            cancellationToken);

        // The comment caused a status change, so the audit trail records it as one too. Without
        // this, an auditor filtering on status changes would not see the move to MissedInfo.
        if (autoTransitionedFrom is { } from)
        {
            await _auditLogger.LogAsync(
                "Application.StatusChanged",
                "Application",
                application.Id,
                new
                {
                    application.ApplicationNumber,
                    From = from,
                    To = application.Status,
                    Reason = "User-visible review comment",
                },
                cancellationToken);
        }

        return new CommentDto(
            comment.Id,
            comment.AuthorType,
            comment.AuthorName,
            comment.Visibility,
            comment.Body,
            comment.CreatedAtUtc);
    }
}

// ------------------------------------------------------- Applicant comments

/// <summary>An applicant's reply. Always user-visible — an applicant cannot write internal notes.</summary>
public sealed record AddApplicantCommentCommand(Guid ApplicationId, string Body) : IRequest<CommentDto>;

public sealed class AddApplicantCommentCommandValidator
    : AbstractValidator<AddApplicantCommentCommand>
{
    public AddApplicantCommentCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.Body).NotEmpty().MaximumLength(4000);
    }
}

public sealed class AddApplicantCommentCommandHandler
    : IRequestHandler<AddApplicantCommentCommand, CommentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public AddApplicantCommentCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<CommentDto> Handle(
        AddApplicantCommentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var application = await _db.Applications
            .FirstOrDefaultAsync(
                a => a.Id == request.ApplicationId && a.OrderId == orderId,
                cancellationToken)
            ?? throw new NotFoundException("Application", request.ApplicationId);

        // Visibility is forced to ForUser inside the factory, so no code path can produce an
        // applicant-authored internal comment.
        var comment = ApplicationComment.FromApplicant(
            application.Id,
            _currentUser.ToActor(),
            request.Body.Trim());

        _db.ApplicationComments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.ApplicantReplied",
            "Application",
            application.Id,
            null,
            cancellationToken);

        return new CommentDto(
            comment.Id,
            comment.AuthorType,
            comment.AuthorName,
            comment.Visibility,
            comment.Body,
            comment.CreatedAtUtc);
    }
}

// ---------------------------------------------------------------- Resubmit

/// <summary>MissedInfo → InProgress once the applicant has supplied what was asked for.</summary>
public sealed record ResubmitApplicationCommand(Guid ApplicationId)
    : IRequest<ApplicationStatusResultDto>;

public sealed class ResubmitApplicationCommandHandler
    : IRequestHandler<ResubmitApplicationCommand, ApplicationStatusResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public ResubmitApplicationCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<ApplicationStatusResultDto> Handle(
        ResubmitApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var application = await _db.Applications
            .FirstOrDefaultAsync(
                a => a.Id == request.ApplicationId && a.OrderId == orderId,
                cancellationToken)
            ?? throw new NotFoundException("Application", request.ApplicationId);

        if (application.Status != ApplicationStatus.MissedInfo)
        {
            throw new ConflictException(
                "application.not_awaiting_information",
                $"Only an application awaiting more information can be resubmitted; this one is '{application.Status}'.");
        }

        application.TransitionTo(
            ApplicationStatus.InProgress,
            _currentUser.ToActor(),
            "Applicant resubmitted the requested information");

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.Resubmitted",
            "Application",
            application.Id,
            new { application.ApplicationNumber },
            cancellationToken);

        return new ApplicationStatusResultDto(
            application.Id,
            application.ApplicationNumber,
            application.Status,
            application.Status.ToString());
    }
}

// ------------------------------------------------------------ Result files

/// <summary>Attaches a verified deliverable. Only meaningful once the application has succeeded.</summary>
public sealed record UploadResultFileCommand(
    Guid ApplicationId,
    string FileName,
    long SizeBytes,
    Stream Content) : IRequest<ResultFileDto>;

public sealed class UploadResultFileCommandHandler
    : IRequestHandler<UploadResultFileCommand, ResultFileDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public UploadResultFileCommandHandler(
        IApplicationDbContext db,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _storage = storage;
        _typeValidator = typeValidator;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<ResultFileDto> Handle(
        UploadResultFileCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await _db.Applications
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException("Application", request.ApplicationId);

        if (application.Status != ApplicationStatus.Success)
        {
            throw new ConflictException(
                "application.results_not_available",
                "Result files can only be attached to an application that has succeeded.");
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > ApplicationFile.MaxFileSizeBytes)
        {
            throw new ConflictException(
                "file.too_large",
                $"Files must be between 1 byte and {ApplicationFile.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        var contentType = await _typeValidator.DetectAllowedContentTypeAsync(
            request.Content,
            request.FileName,
            cancellationToken)
            ?? throw new ConflictException(
                "file.unsupported_type",
                "Only PDF, JPG, JPEG and PNG files are accepted.");

        var storagePath = await _storage.SaveAsync(
            request.Content,
            $"orders/{application.OrderId}/applications/{application.Id}/results",
            request.FileName,
            application.Id.ToString("N"),
            cancellationToken);

        var actor = _currentUser.ToActor();

        var file = new ApplicationFile
        {
            ApplicationId = application.Id,
            FileName = Path.GetFileName(request.FileName),
            StoragePath = storagePath,
            ContentType = contentType,
            SizeBytes = request.SizeBytes,
            Kind = ApplicationFileKind.AdminResult,
            UploadedByType = actor.Type,
            UploadedById = actor.Id,
            UploadedByName = actor.DisplayName,
        };

        _db.ApplicationFiles.Add(file);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.ResultAttached",
            "Application",
            application.Id,
            new { file.FileName, file.SizeBytes },
            cancellationToken);

        return new ResultFileDto(
            file.Id,
            file.FileName,
            file.ContentType,
            file.SizeBytes,
            file.CreatedAtUtc,
            $"/api/v1/applications/{application.Id}/files/{file.Id}",
            DownloadFileName.Compose(
                application.ApplicationNumber,
                application.Order?.OrderNumber,
                null,
                file.FileName));
    }
}

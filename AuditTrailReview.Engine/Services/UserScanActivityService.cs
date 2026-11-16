using AuditTrailReview.Database;
using Microsoft.EntityFrameworkCore;

namespace AuditTrailReview.Engine.Services
{
    public interface IUserScanActivityService
    {
        /// <summary>True if the user has uploaded or queued at least one scan (<c>Scan.UserId</c> or <c>ScanUploadJob.UserId</c>).</summary>
        Task<bool> HasUploadedScansAsync(string userId);

        /// <summary>Batched version of <see cref="HasUploadedScansAsync"/> for a page of users -- two queries total instead of one pair per user.</summary>
        Task<HashSet<string>> GetUserIdsWithScanActivityAsync(IEnumerable<string> userIds);
    }

    public class UserScanActivityService : IUserScanActivityService
    {
        private readonly ApplicationDbContext _context;

        public UserScanActivityService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> HasUploadedScansAsync(string userId)
        {
            return await _context.Scan.AnyAsync(s => s.UserId == userId)
                || await _context.ScanUploadJob.AnyAsync(j => j.UserId == userId);
        }

        public async Task<HashSet<string>> GetUserIdsWithScanActivityAsync(IEnumerable<string> userIds)
        {
            var ids = userIds.ToList();

            var fromScans = await _context.Scan
                .Where(s => s.UserId != null && ids.Contains(s.UserId))
                .Select(s => s.UserId!)
                .ToListAsync();

            var fromJobs = await _context.ScanUploadJob
                .Where(j => j.UserId != null && ids.Contains(j.UserId))
                .Select(j => j.UserId!)
                .ToListAsync();

            return new HashSet<string>(fromScans.Concat(fromJobs));
        }
    }
}

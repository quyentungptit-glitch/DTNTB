using Microsoft.AspNetCore.Http;

namespace DTNTB.Core.DTOs
{
    public class FileUploadResultDto
    {
        public bool Success { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
    }

    public class UploadImageRequest
    {
        public IFormFile File { get; set; } = null!;
        public string Folder { get; set; } = "avatars";
    }
}
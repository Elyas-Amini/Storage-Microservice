﻿using EasyMicroservices.Cores.AspCoreApi;
using EasyMicroservices.Cores.Database.Interfaces;
using EasyMicroservices.FileManager.Interfaces;
using EasyMicroservices.ServiceContracts;
using EasyMicroservices.StorageMicroservice.Contracts;
using EasyMicroservices.StorageMicroservice.Database.Entities;
using Microsoft.AspNetCore.Mvc;

namespace EasyMicroservices.StorageMicroservice.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class FileController : SimpleQueryServiceController<FileEntity, AddFileRequestContract, FileContract, FileContract, long>
    {
        private readonly IDirectoryManagerProvider _directoryManagerProvider;
        private readonly IFileManagerProvider _fileManagerProvider;
        private readonly IContractLogic<FileEntity, AddFileRequestContract, FileContract, FileContract, long> _contractLogic;
        public FileController(IDirectoryManagerProvider directoryManagerProvider, IFileManagerProvider fileManagerProvider, IContractLogic<FileEntity, AddFileRequestContract, FileContract, FileContract, long> contractLogic) : base(contractLogic)
        {
            _directoryManagerProvider = directoryManagerProvider;
            _fileManagerProvider = fileManagerProvider;
            _contractLogic = contractLogic;
        }

        private string NameToFullPath(string fileName)
        {
            string webRootPath = Directory.GetCurrentDirectory();
            string directoryPath = _directoryManagerProvider.PathProvider.Combine(webRootPath, fileName);
            return directoryPath;
        }

        [HttpPost]
        public async Task<MessageContract<FileContract>> UploadFile([FromForm] AddFileRequestContract input)
        {
            var fileExtension = Path.GetExtension(input.File.FileName);

            var filePassword = Guid.NewGuid().ToString();
            var fileName = $"{input.File.FileName}";

            var newFile = new FileEntity
            {
                CreationDateTime = DateTime.Now,
                Name = input.File.FileName,
                ContentType = input.File.ContentType,
                Length = input.File.Length,
                Extension = fileExtension,
                FolderId = input.FolderId ?? 1,
                Password = filePassword,
                Path = NameToFullPath(fileName),
                UniqueIdentity = input.UniqueIdentity
            };

            using var stream = new FileStream(newFile.Path, FileMode.Create);
            await input.File.CopyToAsync(stream);

            var result = await _contractLogic.Add(input);
            if (result.TryGetResult(out long fileId, out MessageContract<FileContract> errorContract))
            {
                return new FileContract
                {
                    Id = result.Result,
                    CreationDateTime = newFile.CreationDateTime,
                    Name = newFile.Name,
                    ContentType = newFile.ContentType,
                    Length = newFile.Length,
                    Extension = newFile.Extension,
                    FolderId = newFile.FolderId,
                    Password = newFile.Password,
                    Path = newFile.Path,
                    DownloadLink = GenerateDownloadLink(HttpContext, newFile.Id, newFile.Password),
                };
            }
            return errorContract;
        }

        [HttpDelete]
        public async Task<MessageContract> DeleteFileByPassword(long fileId, string password)
        {
            var find = await GetById(fileId);
            if (find)
            {
                if (find.Result.Password == password)
                {
                    var filePath = NameToFullPath(find.Result.Path);
                    if (await _fileManagerProvider.IsExistFileAsync(filePath))
                    {
                        await _fileManagerProvider.DeleteFileAsync(filePath);
                    }
                    var deleteResult = await HardDeleteById(fileId);
                    return deleteResult;
                }
                else
                    return (FailedReasonType.ValidationsError, "File id or Password is not valid");
            }
            return find;
        }

        private static string GenerateDownloadLink(HttpContext httpContext, long fileId, string password)
        {
            string DownloadLink = @$"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/File/DownloadFile?id={fileId}&password={password}";
            return DownloadLink;
        }

        [HttpGet]
        public async Task<IActionResult> DownloadFileWithIdAsync([FromQuery] long id, [FromQuery] string password)
        {
            var file = await GetById(id);
            if (!file || file.Result.Password != password)
            {
                return NotFound();
            }
            else
            {
                var filePath = NameToFullPath(file.Result.Path);

                if (!await _fileManagerProvider.IsExistFileAsync(filePath))
                    return NotFound();

                var fileBytes = await _fileManagerProvider.ReadAllBytesAsync(filePath);

                return File(fileBytes, "application/octet-stream", file.Result.Name);
            }
        }
    }
}
using GameShrink.Core.Models;

namespace GameShrink.App.ViewModels;

public sealed class FileRowViewModel
{
    public required FileAnalysisInfo Model { get; init; }

    public string RelativePath => Model.RelativePath;
    public string SizeText => Formatters.Bytes(Model.Size);
    public string EstimatedSavingsText => Formatters.Bytes(Model.EstimatedSavings);
    public string EstimatedRatioText => Model.EstimatedCompressionRatio is > 0 and < 10 ? Model.EstimatedCompressionRatio.ToString("0.00") : "-";
}

public sealed class FolderRowViewModel
{
    public required string Folder { get; init; }
    public required long Savings { get; init; }

    public string SavingsText => Formatters.Bytes(Savings);
}

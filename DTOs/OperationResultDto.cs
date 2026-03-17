namespace Vizora.DTOs
{
    public class OperationResultDto
    {
        public OperationOutcomeStatus Status { get; set; } = OperationOutcomeStatus.Success;

        public string UserMessage { get; set; } = string.Empty;

        public bool IsDataTrusted { get; set; } = true;

        public List<OperationIssueDto> Issues { get; set; } = new();
    }
}

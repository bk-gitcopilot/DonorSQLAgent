using System.ComponentModel.DataAnnotations.Schema;

namespace DonorSQLAgent
{
    public class DocumentChuncksViewModel
    {
        public int DocumentID { get; set; }

        public string DocumentName { get; set; }

        public string DocumentType { get; set; }

        public int? DocumentTotalPage { get; set; }

        public string TextData { get; set; }

        [Column(TypeName = "vector")]
        public Pgvector.Vector? EmbeddedChunkData { get; set; }

        public double? Similarity { get; set; }

        public double? KeywordSimilarity { get; set; }

        public double? CombinedSimilarity { get; set; }
    }
}

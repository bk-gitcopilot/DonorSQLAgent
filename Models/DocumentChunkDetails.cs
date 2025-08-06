using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DonorSQLAgent
{
    public class DocumentChunkDetails
    {
        [Key]
        public int documentchunk_id { get; set; }

        public int document_id { get; set; }

        [ForeignKey("document_id")]
        public virtual DocumentsDetails documentdetails { get; set; }

        [Column(TypeName = "vector(1536)")]
        public Pgvector.Vector? embeddedchunkdata { get; set; }

        [Required]
        public bool isactive { get; set; }

        [Required]
        public DateTime createdat { get; set; }

        [Required]
        public DateTime modifiedat { get; set; }

        [Required]
        public string textdata { get; set; }

        [Required]
        public int pagenumber { get; set; }
    }
}

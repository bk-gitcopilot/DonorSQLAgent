using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DonorSQLAgent
{
    public class DocumentsDetails
    {
        [Key]
        public int document_id { get; set; }

        public string? documentname { get; set; }

        public string? documenttype { get; set; }

        public int? documenttotalpage { get; set; }

        public string? createdby { get; set; }

        public bool isactive { get; set; }

        public DateTime createdat { get; set; }

        public DateTime modifiedat { get; set; }
    }
}

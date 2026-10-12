# This script uses PowerShell to read the documents via Word COM
import subprocess
import os

def read_doc_file(filepath):
    """Read text from a .doc file using PowerShell and Word COM"""
    abs_path = os.path.abspath(filepath)
    script = f'''
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $word.Documents.Open("{abs_path}")
$doc.Content.Text
$doc.Close()
$word.Quit()
'''
    result = subprocess.run(['powershell', '-Command', script], capture_output=True, text=True)
    return result.stdout

# Read original document to get Part 2
print("=" * 80)
print("ORIGINAL DOCUMENT: CLDV6212POE.docx")
print("=" * 80)
text1 = read_doc_file('CLDV6212POE.docx')
print(text1)

$word = New-Object -ComObject Word.Application
$word.Visible = $false

$doc1 = $word.Documents.Open("C:\Users\zaeem\Desktop\-Cloud-Development-B-CLDV6212\CLDV6212POE.docx")
Write-Host "=== CLDV6212POE.docx ==="
Write-Host $doc1.Content.Text
$doc1.Close()

$doc2 = $word.Documents.Open("C:\Users\zaeem\Desktop\-Cloud-Development-B-CLDV6212\Addendum - CLDV6212 POE.docx")
Write-Host "`n=== Addendum - CLDV6212 POE.docx ==="
Write-Host $doc2.Content.Text
$doc2.Close()

$word.Quit()

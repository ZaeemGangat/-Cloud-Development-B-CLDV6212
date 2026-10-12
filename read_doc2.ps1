$word = New-Object -ComObject Word.Application
$word.Visible = $false

$doc1 = $word.Documents.Open("C:\Users\zaeem\Desktop\-Cloud-Development-B-CLDV6212\CLDV6212POE.docx")
$doc1.Content.Text | Out-File -FilePath "C:\Users\zaeem\Desktop\-Cloud-Development-B-CLDV6212\doc1.txt" -Encoding UTF8
$doc1.Close()

$doc2 = $word.Documents.Open("C:\Users\zaeem\Desktop\-Cloud-Development-B-CLDV6212\Addendum - CLDV6212 POE.docx")
$doc2.Content.Text | Out-File -FilePath "C:\Users\zaeem\Desktop\-Cloud-Development-B-CLDV6212\doc2.txt" -Encoding UTF8
$doc2.Close()

$word.Quit()
Write-Host "Documents exported to doc1.txt and doc2.txt"

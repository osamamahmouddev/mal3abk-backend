output "bucket_id" {
  value = aws_s3_bucket.private_storage.id
}

output "bucket_arn" {
  value = aws_s3_bucket.private_storage.arn
}

output "kms_key_arn" {
  value = aws_kms_key.storage.arn
}

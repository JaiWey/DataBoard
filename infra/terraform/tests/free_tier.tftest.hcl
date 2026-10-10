# Run with:  terraform test        (no AWS account or sign-in needed: the provider is mocked)
# Checks that the database cannot leave the RDS free tier by accident.

mock_provider "aws" {
  mock_data "aws_subnets" {
    defaults = {
      ids = ["subnet-aaaa1111", "subnet-bbbb2222"]
    }
  }

  mock_data "aws_subnet" {
    defaults = {
      id                = "subnet-aaaa1111"
      availability_zone = "ap-southeast-2a"
    }
  }

  mock_data "aws_ec2_instance_type_offerings" {
    defaults = {
      locations = ["ap-southeast-2a", "ap-southeast-2b"]
    }
  }

  mock_data "aws_ssm_parameter" {
    defaults = {
      value = "ami-0123456789abcdef0"
    }
  }

  mock_data "aws_caller_identity" {
    defaults = {
      account_id = "123456789012"
    }
  }

  mock_data "aws_iam_policy_document" {
    defaults = {
      json = "{\"Version\":\"2012-10-17\",\"Statement\":[]}"
    }
  }
}

run "the_defaults_are_inside_the_free_tier" {
  command = plan

  assert {
    condition     = aws_db_instance.db.instance_class == "db.t4g.micro" && aws_db_instance.db.allocated_storage == 20 && aws_db_instance.db.multi_az == false
    error_message = "The default database is not a single-zone db.t4g.micro with 20 GB."
  }

  assert {
    condition     = aws_db_instance.db.storage_type == "gp2" && aws_db_instance.db.max_allocated_storage == null
    error_message = "The database must use gp2 storage that cannot grow by itself."
  }
}

run "a_bigger_instance_is_refused" {
  command = plan

  variables {
    db_instance_class = "db.m6g.large"
  }

  expect_failures = [aws_db_instance.db]
}

run "more_storage_is_refused" {
  command = plan

  variables {
    db_storage_gb = 100
  }

  expect_failures = [aws_db_instance.db]
}

run "a_bigger_database_is_allowed_when_asked_for" {
  command = plan

  variables {
    db_instance_class   = "db.m6g.large"
    db_storage_gb       = 100
    allow_paid_database = true
  }

  assert {
    condition     = aws_db_instance.db.instance_class == "db.m6g.large"
    error_message = "allow_paid_database should let a bigger database through."
  }
}

run "another_free_tier_class_is_allowed" {
  command = plan

  variables {
    db_instance_class = "db.t3.micro"
  }

  assert {
    condition     = aws_db_instance.db.instance_class == "db.t3.micro"
    error_message = "db.t3.micro is in the free tier."
  }
}

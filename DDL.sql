-- Drop tables if they exist
DROP TABLE IF EXISTS `common_user`;
DROP TABLE IF EXISTS `administration_organization`;

-- Create administration_organization first (parent table)
CREATE TABLE `administration_organization` (
  `organization_id` int unsigned NOT NULL AUTO_INCREMENT,
  `organization_code` char(3) NOT NULL,
  `organization_iata_code` char(2) DEFAULT NULL,
  `organization_name` varchar(64) NOT NULL,
  `organization_type_id` int unsigned NOT NULL,
  PRIMARY KEY (`organization_id`),
  UNIQUE KEY `organization_code` (`organization_code`),
  UNIQUE KEY `organization_name` (`organization_name`),
  KEY `fk_AdminOrganization_OrganizationType_OrganizationTypeID` (`organization_type_id`),
  CONSTRAINT `ORGANIZATION_CODE_NUM_NOT_ALLOWED`
    CHECK (
      regexp_like(`organization_code`, _utf8mb4'^([^0-9]*)$')
      AND length(`organization_code`) = 3
    ),
  CONSTRAINT `ORGANIZATION_IATA_LENGTH`
    CHECK (length(`organization_iata_code`) = 2)
) ENGINE=InnoDB
  AUTO_INCREMENT=53
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

-- Create common_user
CREATE TABLE `common_user` (
  `user_id` int unsigned NOT NULL AUTO_INCREMENT,
  `first_name` varchar(255) NOT NULL,
  `last_name` varchar(255) NOT NULL,
  `display_name` varchar(50) NOT NULL,
  `user_email` varchar(255) NOT NULL,
  `user_admin` tinyint(1) NOT NULL,
  `AD_user` tinyint(1) NOT NULL,
  `status` varchar(255) DEFAULT NULL,
  `business` int unsigned NOT NULL,
  `user_email_lower` varchar(255)
    GENERATED ALWAYS AS (LOWER(`user_email`)) VIRTUAL,
  PRIMARY KEY (`user_id`),
  UNIQUE KEY `uniq_UserEmail` (`user_email`),
  KEY `fk_CommonUser_AdminOrganization_OrganizationID` (`business`),
  KEY `idx_user_email_lower` (`user_email_lower`)
) ENGINE=InnoDB
  AUTO_INCREMENT=51
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;
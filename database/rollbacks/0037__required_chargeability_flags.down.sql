ALTER TABLE `appointment_types`
    ALTER COLUMN `requires_payment` SET DEFAULT 1;

ALTER TABLE `appointments`
    ALTER COLUMN `requires_payment` SET DEFAULT 1;

ALTER TABLE `payments`
    ALTER COLUMN `appointment_requires_payment` SET DEFAULT 1;

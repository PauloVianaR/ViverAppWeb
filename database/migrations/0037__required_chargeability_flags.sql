-- Fase 19. Mantém flags de cobrança obrigatórias no modelo DB-First.
-- O padrão true pertence aos casos de uso; o banco exige intenção explícita em cada nova linha.

ALTER TABLE `appointment_types`
    ALTER COLUMN `requires_payment` DROP DEFAULT;

ALTER TABLE `appointments`
    ALTER COLUMN `requires_payment` DROP DEFAULT;

ALTER TABLE `payments`
    ALTER COLUMN `appointment_requires_payment` DROP DEFAULT;


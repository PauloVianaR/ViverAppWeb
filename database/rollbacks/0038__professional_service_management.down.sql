DELETE FROM application_settings
WHERE setting_key = 'manager.professional_services_enabled';

-- As preferências e vínculos reparados não são removidos: podem ter sido alterados
-- ou utilizados após a aplicação e sua exclusão destruiria dados operacionais.

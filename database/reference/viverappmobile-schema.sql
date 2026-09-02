-- Snapshot estrutural somente leitura de viverappmobile.
-- Restaure somente em servidor isolado, após selecionar um database vazio e descartável.
-- Nunca execute este arquivo em viverappmobile ou viverappweb.
-- Nenhum registro do legado está incluído.
SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS `appointment`;
CREATE TABLE `appointment` (
  `idappointment` int NOT NULL AUTO_INCREMENT,
  `idappointmenttype` int NOT NULL,
  `title` varchar(150) DEFAULT NULL,
  `description` text,
  `averagetime` time DEFAULT NULL,
  `price` decimal(12,2) DEFAULT NULL,
  `ispopular` tinyint NOT NULL DEFAULT '0',
  `canonline` tinyint NOT NULL DEFAULT '0',
  `status` int NOT NULL DEFAULT '1',
  PRIMARY KEY (`idappointment`),
  UNIQUE KEY `title_UNIQUE` (`title`),
  KEY `fk_atendimento_tipoatendimento_idx` (`idappointmenttype`),
  CONSTRAINT `fk_appointment_appointmenttype` FOREIGN KEY (`idappointmenttype`) REFERENCES `appointment_type` (`idappointmenttype`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `appointment_type`;
CREATE TABLE `appointment_type` (
  `idappointmenttype` int NOT NULL AUTO_INCREMENT,
  `description` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`idappointmenttype`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `availability_clinic`;
CREATE TABLE `availability_clinic` (
  `idavailabilityclinic` int NOT NULL AUTO_INCREMENT,
  `idclinic` int NOT NULL,
  `daytype` int DEFAULT NULL,
  `starttime` time DEFAULT NULL,
  `endtime` time DEFAULT NULL,
  PRIMARY KEY (`idavailabilityclinic`),
  KEY `fk_availabilityclinic_clinic_idx` (`idclinic`),
  CONSTRAINT `fk_availabilityclinic_clinic` FOREIGN KEY (`idclinic`) REFERENCES `clinic` (`idclinic`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `availability_doctor`;
CREATE TABLE `availability_doctor` (
  `idavailabilitydoctor` int NOT NULL AUTO_INCREMENT,
  `iddoctor` int NOT NULL,
  `daytype` int DEFAULT NULL,
  `starttime` time DEFAULT NULL,
  `endtime` time DEFAULT NULL,
  `isonline` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`idavailabilitydoctor`),
  KEY `fk_availabilitydoctor_doctor_idx` (`iddoctor`),
  CONSTRAINT `fk_availabilitydoctor_doctor` FOREIGN KEY (`iddoctor`) REFERENCES `user` (`iduser`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `clinic`;
CREATE TABLE `clinic` (
  `idclinic` int NOT NULL AUTO_INCREMENT,
  `corporatereason` varchar(90) DEFAULT NULL,
  `fantasyname` varchar(90) DEFAULT NULL,
  `cnpj` varchar(18) DEFAULT NULL,
  `email` varchar(45) DEFAULT NULL,
  `adress` varchar(45) DEFAULT NULL,
  `number` varchar(4) DEFAULT NULL,
  `neighborhood` varchar(45) DEFAULT NULL,
  `complement` varchar(45) DEFAULT NULL,
  `city` varchar(45) DEFAULT NULL,
  `state` varchar(2) DEFAULT NULL,
  `fone` varchar(15) DEFAULT NULL,
  `postalcode` varchar(9) DEFAULT NULL,
  PRIMARY KEY (`idclinic`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `config`;
CREATE TABLE `config` (
  `idconfig` int NOT NULL AUTO_INCREMENT,
  `name` varchar(100) DEFAULT NULL,
  `description` varchar(512) DEFAULT NULL,
  `value` int DEFAULT NULL,
  `valueisbool` tinyint NOT NULL DEFAULT '0',
  `canshow` tinyint DEFAULT '1',
  PRIMARY KEY (`idconfig`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `doctor_props`;
CREATE TABLE `doctor_props` (
  `iddoctorprops` int NOT NULL AUTO_INCREMENT,
  `iddoctor` int NOT NULL,
  `title` varchar(4) DEFAULT NULL,
  `crm` varchar(15) DEFAULT NULL,
  `mainspecialty` varchar(45) DEFAULT NULL,
  `medicalexperience` int DEFAULT NULL,
  `rating` float(2,2) DEFAULT NULL,
  `attendonline` tinyint NOT NULL DEFAULT '0',
  `maxonlinedayconsultation` int NOT NULL DEFAULT '0',
  `maxpresencialdayconsultation` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`iddoctorprops`),
  UNIQUE KEY `iddoctor_UNIQUE` (`iddoctor`),
  KEY `fk_doctorprops_user_idx` (`iddoctor`),
  CONSTRAINT `fk_doctorprops_user` FOREIGN KEY (`iddoctor`) REFERENCES `user` (`iduser`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `email_confirmation`;
CREATE TABLE `email_confirmation` (
  `idemailconfirmation` int NOT NULL AUTO_INCREMENT,
  `idemail` int NOT NULL,
  `confirmationcode` int NOT NULL,
  `expiresat` datetime NOT NULL,
  PRIMARY KEY (`idemailconfirmation`),
  UNIQUE KEY `idemail_UNIQUE` (`idemail`),
  KEY `fk_emailconfirmation_emailqueue_idx` (`idemail`),
  CONSTRAINT `fk_emailconfirmation_emailqueue` FOREIGN KEY (`idemail`) REFERENCES `email_queue` (`idemail`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `email_queue`;
CREATE TABLE `email_queue` (
  `idemail` int NOT NULL AUTO_INCREMENT,
  `sender` varchar(45) NOT NULL,
  `receiver` varchar(45) NOT NULL,
  `subject` tinytext NOT NULL,
  `body` longtext NOT NULL,
  `severity` int NOT NULL DEFAULT '3',
  `status` int NOT NULL DEFAULT '1',
  `tries` int NOT NULL DEFAULT '0',
  `createdat` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`idemail`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `holiday`;
CREATE TABLE `holiday` (
  `idholiday` int NOT NULL AUTO_INCREMENT,
  `holidayname` varchar(45) DEFAULT NULL,
  `holidaydate` date DEFAULT NULL,
  `canschedule` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`idholiday`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `notification`;
CREATE TABLE `notification` (
  `idnotification` int NOT NULL AUTO_INCREMENT,
  `notificationtype` int NOT NULL DEFAULT '1',
  `severity` int NOT NULL DEFAULT '0',
  `title` varchar(512) DEFAULT NULL,
  `description` varchar(512) DEFAULT NULL,
  `pushdescription` varchar(512) DEFAULT NULL,
  `createdat` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `read` tinyint NOT NULL DEFAULT '0',
  `sent` tinyint NOT NULL DEFAULT '0',
  `targetid` int DEFAULT NULL,
  PRIMARY KEY (`idnotification`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `payment`;
CREATE TABLE `payment` (
  `idpayment` int NOT NULL AUTO_INCREMENT,
  `idpaymenttype` int NOT NULL,
  `idschedule` int NOT NULL,
  `paidday` datetime DEFAULT NULL,
  `paidprice` decimal(12,2) DEFAULT NULL,
  `paidonline` tinyint NOT NULL DEFAULT '0',
  `cardlast4` varchar(4) DEFAULT NULL,
  `cardauthorization` varchar(512) DEFAULT NULL,
  PRIMARY KEY (`idpayment`),
  KEY `fk_payment_paymenttype_idx` (`idpaymenttype`),
  KEY `fk_payment_schedule_idx` (`idschedule`),
  CONSTRAINT `fk_payment_paymenttype` FOREIGN KEY (`idpaymenttype`) REFERENCES `payment_type` (`idpaymenttype`),
  CONSTRAINT `fk_payment_schedule` FOREIGN KEY (`idschedule`) REFERENCES `schedule` (`idschedule`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `payment_type`;
CREATE TABLE `payment_type` (
  `idpaymenttype` int NOT NULL AUTO_INCREMENT,
  `description` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`idpaymenttype`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `premium_plan`;
CREATE TABLE `premium_plan` (
  `idpremiumplan` int NOT NULL AUTO_INCREMENT,
  `price` decimal(12,2) DEFAULT NULL,
  `plantype` int DEFAULT NULL,
  `testperiod` varchar(2) DEFAULT NULL,
  PRIMARY KEY (`idpremiumplan`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `premium_user`;
CREATE TABLE `premium_user` (
  `idpremium_user` int NOT NULL AUTO_INCREMENT,
  `iduser` int NOT NULL,
  `idpremiumplan` int NOT NULL,
  `premiumdate` datetime DEFAULT NULL,
  `intestperiod` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`idpremium_user`),
  KEY `fk_premiumuser_user_idx` (`iduser`),
  KEY `fk_premiumuser_premiumplan_idx` (`idpremiumplan`),
  CONSTRAINT `fk_premiumuser_premiumplan` FOREIGN KEY (`idpremiumplan`) REFERENCES `premium_plan` (`idpremiumplan`),
  CONSTRAINT `fk_premiumuser_user` FOREIGN KEY (`iduser`) REFERENCES `user` (`iduser`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `schedule`;
CREATE TABLE `schedule` (
  `idschedule` int NOT NULL AUTO_INCREMENT,
  `idappointment` int NOT NULL,
  `iduser` int NOT NULL,
  `iddoctor` int NOT NULL,
  `idclinic` int NOT NULL,
  `status` int NOT NULL DEFAULT '1',
  `appointmentdate` datetime DEFAULT NULL,
  `obs` text,
  `isonline` tinyint NOT NULL DEFAULT '0',
  `callconcluded` tinyint DEFAULT NULL,
  `rescheduled` tinyint NOT NULL DEFAULT '0',
  `originaldate` datetime DEFAULT NULL,
  `pendingpayment` tinyint NOT NULL DEFAULT '1',
  `rating` float DEFAULT NULL,
  `medicalreport` text,
  `feedback` text,
  `createdat` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`idschedule`),
  KEY `fk_appointmentuser_appointment_idx` (`idappointment`),
  KEY `fk_appointmentuser_user_idx` (`iduser`),
  KEY `fk_appointmentuser_doctor_idx` (`iddoctor`),
  KEY `fk_appointment_clinic_idx` (`idclinic`),
  CONSTRAINT `fk_appointment_clinic` FOREIGN KEY (`idclinic`) REFERENCES `clinic` (`idclinic`),
  CONSTRAINT `fk_appointmentuser_appointment` FOREIGN KEY (`idappointment`) REFERENCES `appointment` (`idappointment`),
  CONSTRAINT `fk_appointmentuser_doctor` FOREIGN KEY (`iddoctor`) REFERENCES `user` (`iduser`),
  CONSTRAINT `fk_appointmentuser_user` FOREIGN KEY (`iduser`) REFERENCES `user` (`iduser`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `schedule_attachments`;
CREATE TABLE `schedule_attachments` (
  `idscheduleattachments` int NOT NULL AUTO_INCREMENT,
  `idschedule` int NOT NULL,
  `filepath` varchar(512) NOT NULL,
  `filename` varchar(512) NOT NULL,
  `size` float DEFAULT NULL,
  PRIMARY KEY (`idscheduleattachments`),
  KEY `fk_scheduleattachments_schedule_idx` (`idschedule`),
  CONSTRAINT `fk_scheduleattachments_schedule` FOREIGN KEY (`idschedule`) REFERENCES `schedule` (`idschedule`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `specialtys_doctor`;
CREATE TABLE `specialtys_doctor` (
  `idspecialtysdoctor` int NOT NULL AUTO_INCREMENT,
  `iddoctor` int DEFAULT NULL,
  `idappointment` int DEFAULT NULL,
  PRIMARY KEY (`idspecialtysdoctor`),
  KEY `fk_specialtysdoctor_doctor_idx` (`iddoctor`),
  KEY `fk_specialtysdoctor_appointment_idx` (`idappointment`),
  CONSTRAINT `fk_specialtysdoctor_appointment` FOREIGN KEY (`idappointment`) REFERENCES `appointment` (`idappointment`),
  CONSTRAINT `fk_specialtysdoctor_doctor` FOREIGN KEY (`iddoctor`) REFERENCES `user` (`iduser`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `user`;
CREATE TABLE `user` (
  `iduser` int NOT NULL AUTO_INCREMENT,
  `usertype` int NOT NULL,
  `name` varchar(45) DEFAULT NULL,
  `email` varchar(45) DEFAULT NULL,
  `fone` varchar(15) DEFAULT NULL,
  `birthdate` date DEFAULT NULL,
  `password` varchar(128) DEFAULT NULL,
  `status` int NOT NULL,
  `ispremium` tinyint DEFAULT NULL,
  `notifyemail` tinyint NOT NULL DEFAULT '0',
  `notifypush` tinyint NOT NULL DEFAULT '0',
  `cpf` varchar(14) DEFAULT NULL,
  `adress` varchar(45) DEFAULT NULL,
  `neighborhood` varchar(45) DEFAULT NULL,
  `number` varchar(45) DEFAULT NULL,
  `city` varchar(45) DEFAULT NULL,
  `state` varchar(2) DEFAULT NULL,
  `postalcode` varchar(9) DEFAULT NULL,
  `complement` varchar(45) DEFAULT NULL,
  `createdat` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `devicetoken` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`iduser`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `user_token`;
CREATE TABLE `user_token` (
  `idusertoken` int NOT NULL AUTO_INCREMENT,
  `iduser` int NOT NULL,
  `token` varchar(512) NOT NULL,
  `created_at` datetime NOT NULL,
  `expires_at` datetime NOT NULL,
  `revoked` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`idusertoken`),
  KEY `fk_usertokens_user_idx` (`iduser`),
  CONSTRAINT `fk_usertokens_user` FOREIGN KEY (`iduser`) REFERENCES `user` (`iduser`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DELIMITER $$
DROP EVENT IF EXISTS `CancelarAtendimentosPendentes`$$
CREATE EVENT `CancelarAtendimentosPendentes` ON SCHEDULE EVERY 1 DAY STARTS '2025-01-01 23:00:00' ON COMPLETION NOT PRESERVE DISABLE DO begin
	set sql_safe_updates = 0;
	insert notification(notificationtype,severity,title,`description`,pushdescription,createdat,`read`,sent,targetid)
    select 5,3,'Atendimento cancelado pelo sistema',
    concat('Médico: ',d.name,'\nPaciente: ',u.name,'\nAtendimento:',a.title,'\nMotivo cancelamento: Pendente a mais de 24hrs') as `Description`,
    concat(u.name,', seu atendimento com ',dp.title,' ',d.name,' que estava marcado para ',date_format(s.appointmentdate,'%d/%m/%Y'), ' às ',date_format(s.appointmentdate,'%H:%i'),' foi cancelado por falta de confirmação ou pagamento.\n Caso ainda esteja interessado em marcar outro atendimento, poderá acessar o aplicativo Viver e realizar o agendamento.') as `Pushdescription`,
    now(),0,0,u.iduser
    from schedule s
    inner join appointment a on a.idappointment = s.idappointment
    inner join user u on u.iduser = s.iduser
    inner join user d on d.iduser = s.iddoctor
    inner join doctor_props dp on dp.iddoctor = d.iduser
    where s.status = 1
    and s.createdat <= NOW() - INTERVAL 24 HOUR;

    update schedule
    set status = 4,
    feedback = 'Cancelado automaticamento por falta de confirmação ou pagamento'
    where status = 1
    and createdat <= NOW() - INTERVAL 24 HOUR;
end$$
DELIMITER ;

SET FOREIGN_KEY_CHECKS = 1;

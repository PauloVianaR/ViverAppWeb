-- Restringe o catálogo aos quatro papéis aprovados para o produto.

ALTER TABLE `roles`
    ADD CONSTRAINT `ck_roles_code`
    CHECK (`code` IN ('administrator', 'doctor', 'manager', 'patient'));

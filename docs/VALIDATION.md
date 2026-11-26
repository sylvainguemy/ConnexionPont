# Validation

## Vérifié dans l'environnement de développement

- Compilation des deux interfaces Windows avec le compilateur .NET Framework intégré.
- Génération de certificats RSA, empreintes correctes et rejet d'une empreinte différente.
- Export/import du profil chiffré, absence de clé privée relais dans l'export.
- Refus d'un mauvais mot de passe, d'un profil modifié et d'un profil trop volumineux.
- Enregistrement / lecture de la configuration protégée DPAPI.

## Tests réseau automatisés

`scripts/test.ps1` lance le parcours TLS complet avec deux services TCP de test, vérifie l'intégrité binaire, la séparation des routes, les sessions concurrentes, le rejet d'un agent non appairé et d'une session inconnue, la fermeture, puis la reconnexion.

L'environnement local de développement bloque les connexions TCP, même vers la boucle locale (`WSAEACCES`). Les résultats complets réseau doivent être consultés dans le workflow Windows GitHub. La présence d'un EXE compilé ne signifie pas que les tests réseau ont réussi. Le workflow ne publie son ZIP que si tous les tests passent.

## Qualification à effectuer chez l'utilisateur

- Freebox : plage IPv4 / full-stack, redirection, pare-feu, connexion depuis un réseau externe.
- PC client : édition Windows compatible RDP, service, autorisations et compte Windows.
- Serveur SSH : adresse interne réelle, port 22 joignable depuis le PC client, authentification et empreinte du serveur.
- RDP et SSH réels, veille / réveil, réseau du client et éventuel proxy / inspection TLS.
- Installation et désinstallation avec les droits du poste cible ; EXE non signés et politiques locales.

V0.1 : pas d'audit indépendant et pas de validation de production implicite.

/**
 * Script one-shot — crée un admin Qualité KCQ.
 * Mot de passe OBLIGATOIRE via env (jamais de défaut commité).
 *
 *   MONGO_URI=... ADMIN_EMAIL=... ADMIN_PASSWORD=... node createAdmin.js
 */
require('dotenv').config();
const mongoose = require('mongoose');
const bcrypt = require('bcryptjs');
const User = require('./models/User');

async function createAdmin() {
  try {
    const email = (process.env.ADMIN_EMAIL || '').trim();
    const password = process.env.ADMIN_PASSWORD || '';
    if (!email || password.length < 12) {
      console.error(
        'Refusé : définir ADMIN_EMAIL et ADMIN_PASSWORD (>= 12 caractères). Aucun mot de passe par défaut.',
      );
      process.exit(1);
    }

    await mongoose.connect(process.env.MONGO_URI);
    console.log('Connecté à MongoDB');

    const passwordHash = await bcrypt.hash(password, 10);
    const admin = new User({
      name: 'Admin Principal',
      email,
      passwordHash,
      role: 'admin',
    });

    await admin.save();
    console.log(`Admin créé : ${email} (mot de passe non affiché)`);

    mongoose.connection.close();
  } catch (err) {
    console.error('Erreur :', err.message);
    process.exit(1);
  }
}

createAdmin();
